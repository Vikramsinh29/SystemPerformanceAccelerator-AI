import assert from "node:assert/strict";
import test from "node:test";
import {
  LicensingIdentityError,
  createLicensingIdentityBridge
} from "../src/licensing-identity-bridge.js";

test("resolved authenticated account becomes Licensing V2 identity", async () => {
  const request = new Request("https://example.test/api/licenses");
  let seenRequest = null;
  const bridge = createLicensingIdentityBridge({
    resolveAuthenticatedAccount: async value => {
      seenRequest = value;
      return { accountId: "acct-123" };
    },
    productId: "pcspa-pro"
  });

  const identity = await bridge.resolve(request);
  assert.equal(seenRequest, request);
  assert.deepEqual(identity, { accountId: "acct-123", productId: "pcspa-pro" });
  assert.equal(Object.isFrozen(identity), true);
});

test("authenticated account may provide an explicit product mapping", async () => {
  const bridge = createLicensingIdentityBridge({
    resolveAuthenticatedAccount: async () => ({
      accountId: "acct-123",
      productId: "pcspa-enterprise"
    })
  });

  assert.deepEqual(await bridge.resolve(new Request("https://example.test")), {
    accountId: "acct-123",
    productId: "pcspa-enterprise"
  });
});

test("unauthenticated resolver result fails with a stable identity error", async () => {
  const bridge = createLicensingIdentityBridge({
    resolveAuthenticatedAccount: async () => null
  });

  await assert.rejects(
    () => bridge.resolve(new Request("https://example.test")),
    error => error instanceof LicensingIdentityError && error.code === "unauthenticated"
  );
});

test("malformed authenticated account is rejected", async () => {
  const bridge = createLicensingIdentityBridge({
    resolveAuthenticatedAccount: async () => ({ accountId: "" })
  });

  await assert.rejects(
    () => bridge.resolve(new Request("https://example.test")),
    /account\.accountId is required/
  );
});

test("request-controlled identity fields are ignored by the bridge", async () => {
  const request = new Request("https://example.test/api/licenses/activate", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({
      accountId: "attacker-controlled-account",
      productId: "attacker-controlled-product"
    })
  });

  const bridge = createLicensingIdentityBridge({
    resolveAuthenticatedAccount: async () => ({ accountId: "acct-session" }),
    productId: "pcspa-pro"
  });

  assert.deepEqual(await bridge.resolve(request), {
    accountId: "acct-session",
    productId: "pcspa-pro"
  });
});

test("bridge construction rejects missing resolver", () => {
  assert.throws(
    () => createLicensingIdentityBridge({}),
    /resolveAuthenticatedAccount must be a function/
  );
});
