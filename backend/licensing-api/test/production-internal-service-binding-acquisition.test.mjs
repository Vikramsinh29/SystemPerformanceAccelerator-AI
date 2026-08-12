import test from "node:test";
import assert from "node:assert/strict";

import productionEntrypoint from "../src/production-entrypoint.js";
import {
  createProductionInternalTokenAcquisitionHandler,
  isProductionInternalTokenAcquisitionRequest
} from "../src/production-internal-token-acquisition.js";

const STRONG_SECRET = "s".repeat(64);

function makeInternalRequest(body, init = {}) {
  return new Request("https://licensing-v2.internal/internal/token-acquisition", {
    method: "POST",
    headers: {
      "content-type": "application/json; charset=utf-8",
      ...(init.headers ?? {})
    },
    body: JSON.stringify(body)
  });
}

test("internal acquisition request matcher accepts only the service-binding URL shape", () => {
  assert.equal(
    isProductionInternalTokenAcquisitionRequest(
      new Request("https://licensing-v2.internal/internal/token-acquisition")
    ),
    true
  );

  assert.equal(
    isProductionInternalTokenAcquisitionRequest(
      new Request("https://pc-spa-licensing-v2-production.pc-spa-feedback.workers.dev/internal/token-acquisition")
    ),
    false
  );

  assert.equal(
    isProductionInternalTokenAcquisitionRequest(
      new Request("https://licensing-v2.internal/account/license")
    ),
    false
  );
});

test("internal service-binding acquisition issues a bounded production bearer token", async () => {
  const handler = createProductionInternalTokenAcquisitionHandler({
    identitySecret: STRONG_SECRET,
    clock: () => 1_700_000_000,
    lifetimeSeconds: 300
  });

  const response = await handler.fetch(
    makeInternalRequest({
      accountId: "user-service-binding-1",
      productId: "pcspa-pro"
    })
  );

  assert.equal(response.status, 200);
  const body = await response.json();
  assert.equal(body.tokenType, "Bearer");
  assert.equal(body.expiresInSeconds, 300);
  assert.match(body.token, /^pcspa1\./);
});

test("internal service-binding acquisition rejects malformed trusted identity payloads", async () => {
  const handler = createProductionInternalTokenAcquisitionHandler({
    identitySecret: STRONG_SECRET
  });

  const response = await handler.fetch(
    makeInternalRequest({
      accountId: "",
      productId: "pcspa-pro"
    })
  );

  assert.equal(response.status, 503);
  assert.deepEqual(await response.json(), { error: "identity_unavailable" });
});

test("internal service-binding acquisition rejects non-json requests", async () => {
  const handler = createProductionInternalTokenAcquisitionHandler({
    identitySecret: STRONG_SECRET
  });

  const response = await handler.fetch(
    new Request("https://licensing-v2.internal/internal/token-acquisition", {
      method: "POST",
      headers: { "content-type": "text/plain" },
      body: "not-json"
    })
  );

  assert.equal(response.status, 503);
  assert.deepEqual(await response.json(), { error: "identity_unavailable" });
});

test("production entrypoint keeps public Worker host from reaching internal token acquisition", async () => {
  const publicRequest = new Request(
    "https://pc-spa-licensing-v2-production.pc-spa-feedback.workers.dev/internal/token-acquisition",
    {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ accountId: "user-public", productId: "pcspa-pro" })
    }
  );

  const response = await productionEntrypoint.fetch(publicRequest, {
    PRODUCTION_LICENSING_ENABLED: "enabled",
    LICENSING_IDENTITY_SECRET: STRONG_SECRET,
    LICENSING_DB: {
      prepare() {
        throw new Error("public token path must not reach licensing runtime data access");
      },
      async batch() {
        throw new Error("public token path must not reach licensing runtime batch access");
      }
    }
  });

  assert.equal(response.status, 404);
  assert.deepEqual(await response.json(), { error: "not_found" });
});

test("production entrypoint allows the exact internal service-binding request when enabled", async () => {
  const response = await productionEntrypoint.fetch(
    makeInternalRequest({
      accountId: "user-service-binding-2",
      productId: "pcspa-pro"
    }),
    {
      PRODUCTION_LICENSING_ENABLED: "enabled",
      LICENSING_IDENTITY_SECRET: STRONG_SECRET,
      LICENSING_DB: {
        prepare() {
          return {
            bind() {
              return {
                async first() {
                  return null;
                },
                async run() {
                  return { success: true };
                },
                async all() {
                  return { results: [] };
                }
              };
            }
          };
        },
        async batch() {
          return [];
        }
      }
    }
  );

  assert.equal(response.status, 200);
  const body = await response.json();
  assert.equal(body.tokenType, "Bearer");
  assert.equal(body.expiresInSeconds, 300);
  assert.match(body.token, /^pcspa1\./);
});
