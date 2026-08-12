import assert from "node:assert/strict";
import test from "node:test";
import { createProductionIdentityResolver } from "../src/production-identity-verifier.js";
import { createProductionTokenAcquisitionComposition } from "../src/production-token-acquisition-composition.js";

const secret = "production-composition-test-secret-0123456789abcdef";
const nowMs = Date.parse("2026-08-12T08:00:00Z");

function request(body) {
  return new Request("https://internal.invalid/token", {
    method: "POST",
    headers: { "content-type": "application/json", "x-account-id": "acct-attacker" },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
}

test("composition issues a short-lived token from trusted verified session", async () => {
  const handler = createProductionTokenAcquisitionComposition({
    verifySession: async () => ({ accountId: "acct-trusted-1", productId: "pcspa-pro" }),
    identitySecret: secret,
    clock: () => nowMs,
    lifetimeSeconds: 300
  });

  const response = await handler.fetch(request());
  assert.equal(response.status, 200);

  const body = await response.json();
  assert.equal(body.tokenType, "Bearer");
  assert.equal(body.expiresInSeconds, 300);

  const resolve = createProductionIdentityResolver({ secret, clock: () => nowMs });
  const identity = await resolve(new Request("https://licensing.invalid/account/license", {
    headers: { authorization: `Bearer ${body.token}` }
  }));

  assert.deepEqual(identity, {
    accountId: "acct-trusted-1",
    productId: "pcspa-pro"
  });
});

test("composition ignores caller-controlled account and product identity", async () => {
  const handler = createProductionTokenAcquisitionComposition({
    verifySession: async () => ({ accountId: "acct-trusted-2", productId: "pcspa-pro" }),
    identitySecret: secret,
    clock: () => nowMs
  });

  const response = await handler.fetch(request({
    accountId: "acct-attacker",
    productId: "attacker-product"
  }));

  assert.equal(response.status, 200);
  const body = await response.json();

  const resolve = createProductionIdentityResolver({ secret, clock: () => nowMs });
  const identity = await resolve(new Request("https://licensing.invalid/account/license", {
    headers: { authorization: `Bearer ${body.token}` }
  }));

  assert.deepEqual(identity, {
    accountId: "acct-trusted-2",
    productId: "pcspa-pro"
  });
});

test("composition maps missing verified session to unauthenticated", async () => {
  const handler = createProductionTokenAcquisitionComposition({
    verifySession: async () => null,
    identitySecret: secret
  });

  const response = await handler.fetch(request());
  assert.equal(response.status, 401);
  assert.deepEqual(await response.json(), { error: "unauthenticated" });
});

test("composition fails closed when session verification fails", async () => {
  const handler = createProductionTokenAcquisitionComposition({
    verifySession: async () => { throw new Error("session backend unavailable"); },
    identitySecret: secret
  });

  const response = await handler.fetch(request());
  assert.equal(response.status, 503);
  assert.deepEqual(await response.json(), { error: "identity_unavailable" });
});

test("composition preserves POST-only token acquisition boundary", async () => {
  const handler = createProductionTokenAcquisitionComposition({
    verifySession: async () => ({ accountId: "acct-trusted-3" }),
    identitySecret: secret
  });

  const response = await handler.fetch(new Request("https://internal.invalid/token", { method: "GET" }));
  assert.equal(response.status, 405);
  assert.equal(response.headers.get("allow"), "POST");
  assert.deepEqual(await response.json(), { error: "method_not_allowed" });
});

test("composition refuses invalid construction dependencies", () => {
  assert.throws(
    () => createProductionTokenAcquisitionComposition({ identitySecret: secret }),
    /verifySession must be a function/
  );

  assert.throws(
    () => createProductionTokenAcquisitionComposition({ verifySession: async () => null, identitySecret: "short" }),
    /at least 32 characters/
  );
});
