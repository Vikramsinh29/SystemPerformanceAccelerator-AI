import assert from "node:assert/strict";
import test from "node:test";
import { createProductionTokenIssuer } from "../src/production-token-issuer.js";
import { createProductionIdentityResolver } from "../src/production-identity-verifier.js";

const secret = "production-identity-test-secret-0123456789abcdef";
const nowMs = Date.parse("2026-08-12T06:30:00Z");

function request(token) {
  return new Request("https://licensing.invalid/account/license", {
    headers: { authorization: `Bearer ${token}` }
  });
}

test("issuer tokens round-trip through production identity verifier", async () => {
  const issue = createProductionTokenIssuer({ secret, clock: () => nowMs, lifetimeSeconds: 300 });
  const resolve = createProductionIdentityResolver({ secret, clock: () => nowMs });

  const token = await issue({ accountId: "acct-production-1", productId: "pcspa-pro" });

  assert.match(token, /^pcspa1\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$/);
  assert.deepEqual(await resolve(request(token)), {
    accountId: "acct-production-1",
    productId: "pcspa-pro"
  });
});

test("issuer omits optional product while preserving trusted account identity", async () => {
  const issue = createProductionTokenIssuer({ secret, clock: () => nowMs });
  const resolve = createProductionIdentityResolver({ secret, clock: () => nowMs });

  const token = await issue({ accountId: "acct-production-2" });

  assert.deepEqual(await resolve(request(token)), {
    accountId: "acct-production-2"
  });
});

test("issuer enforces bounded lifetime and strong secret", () => {
  assert.throws(
    () => createProductionTokenIssuer({ secret: "too-short" }),
    /at least 32 characters/
  );
  assert.throws(
    () => createProductionTokenIssuer({ secret, lifetimeSeconds: 29 }),
    /between 30 and 900/
  );
  assert.throws(
    () => createProductionTokenIssuer({ secret, lifetimeSeconds: 901 }),
    /between 30 and 900/
  );
});

test("issuer rejects empty or oversized identity claims", async () => {
  const issue = createProductionTokenIssuer({ secret, clock: () => nowMs });

  await assert.rejects(() => issue({ accountId: "" }), /accountId/);
  await assert.rejects(() => issue({ accountId: "a".repeat(129) }), /accountId/);
  await assert.rejects(
    () => issue({ accountId: "acct-production-3", productId: "p".repeat(129) }),
    /productId/
  );
});
