import assert from "node:assert/strict";
import test from "node:test";
import { createProductionIdentityResolver } from "../src/production-identity-verifier.js";
import { createProductionTokenAcquisitionHandler } from "../src/production-token-acquisition.js";

const secret = "production-acquisition-test-secret-0123456789abcdef";
const nowMs = Date.parse("2026-08-12T07:30:00Z");

function request({ method = "POST", body } = {}) {
  return new Request("https://issuer.invalid/api/licensing/token", {
    method,
    headers: body === undefined ? undefined : { "content-type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
}

function bearerRequest(token) {
  return new Request("https://licensing.invalid/account/license", {
    headers: { authorization: `Bearer ${token}` }
  });
}

test("authenticated server identity receives a short-lived production bearer token", async () => {
  const handler = createProductionTokenAcquisitionHandler({
    resolveAuthenticatedAccount: async () => ({ accountId: "acct-trusted-1" }),
    identitySecret: secret,
    clock: () => nowMs
  });

  const response = await handler.fetch(request());
  const body = await response.json();

  assert.equal(response.status, 200);
  assert.equal(response.headers.get("cache-control"), "no-store");
  assert.equal(body.tokenType, "Bearer");
  assert.equal(body.expiresInSeconds, 300);
  assert.match(body.token, /^pcspa1\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$/);

  const resolve = createProductionIdentityResolver({ secret, clock: () => nowMs });
  assert.deepEqual(await resolve(bearerRequest(body.token)), {
    accountId: "acct-trusted-1",
    productId: "pcspa-pro"
  });
});

test("request-controlled account and product fields cannot override trusted identity", async () => {
  const handler = createProductionTokenAcquisitionHandler({
    resolveAuthenticatedAccount: async () => ({
      accountId: "acct-trusted-2",
      productId: "pcspa-pro"
    }),
    identitySecret: secret,
    clock: () => nowMs
  });

  const response = await handler.fetch(request({
    body: {
      accountId: "acct-attacker",
      productId: "attacker-product"
    }
  }));
  const body = await response.json();

  assert.equal(response.status, 200);

  const resolve = createProductionIdentityResolver({ secret, clock: () => nowMs });
  assert.deepEqual(await resolve(bearerRequest(body.token)), {
    accountId: "acct-trusted-2",
    productId: "pcspa-pro"
  });
});

test("missing authenticated server identity returns stable 401 without token", async () => {
  const handler = createProductionTokenAcquisitionHandler({
    resolveAuthenticatedAccount: async () => null,
    identitySecret: secret,
    clock: () => nowMs
  });

  const response = await handler.fetch(request());

  assert.equal(response.status, 401);
  assert.deepEqual(await response.json(), { error: "unauthenticated" });
});

test("identity-provider failure returns fail-closed 503 without exposing details", async () => {
  const handler = createProductionTokenAcquisitionHandler({
    resolveAuthenticatedAccount: async () => {
      throw new Error("private upstream failure");
    },
    identitySecret: secret,
    clock: () => nowMs
  });

  const response = await handler.fetch(request());

  assert.equal(response.status, 503);
  assert.deepEqual(await response.json(), { error: "identity_unavailable" });
});

test("token acquisition accepts POST only", async () => {
  const handler = createProductionTokenAcquisitionHandler({
    resolveAuthenticatedAccount: async () => ({ accountId: "acct-trusted-3" }),
    identitySecret: secret,
    clock: () => nowMs
  });

  const response = await handler.fetch(request({ method: "GET" }));

  assert.equal(response.status, 405);
  assert.equal(response.headers.get("allow"), "POST");
  assert.deepEqual(await response.json(), { error: "method_not_allowed" });
});

test("token acquisition creation enforces trusted resolver, strong secret, and bounded lifetime", () => {
  assert.throws(
    () => createProductionTokenAcquisitionHandler({ identitySecret: secret }),
    /resolveAuthenticatedAccount/
  );

  assert.throws(
    () => createProductionTokenAcquisitionHandler({
      resolveAuthenticatedAccount: async () => ({ accountId: "acct" }),
      identitySecret: "too-short"
    }),
    /at least 32 characters/
  );

  assert.throws(
    () => createProductionTokenAcquisitionHandler({
      resolveAuthenticatedAccount: async () => ({ accountId: "acct" }),
      identitySecret: secret,
      lifetimeSeconds: 901
    }),
    /between 30 and 900/
  );
});
