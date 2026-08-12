import assert from "node:assert/strict";
import test from "node:test";
import { createProductionAuthenticatedAccountResolver } from "../src/production-authenticated-account-adapter.js";

function request() {
  return new Request("https://identity.invalid/token", { method: "POST" });
}

test("verified session resolves trusted account and default product", async () => {
  const resolve = createProductionAuthenticatedAccountResolver({
    verifySession: async () => ({ accountId: "acct-trusted-1" })
  });

  assert.deepEqual(await resolve(request()), {
    accountId: "acct-trusted-1",
    productId: "pcspa-pro"
  });
});

test("verified session may supply a trusted product claim", async () => {
  const resolve = createProductionAuthenticatedAccountResolver({
    verifySession: async () => ({
      accountId: "acct-trusted-2",
      productId: "pcspa-enterprise"
    })
  });

  assert.deepEqual(await resolve(request()), {
    accountId: "acct-trusted-2",
    productId: "pcspa-enterprise"
  });
});

test("missing session resolves to unauthenticated null", async () => {
  const resolve = createProductionAuthenticatedAccountResolver({
    verifySession: async () => null
  });

  assert.equal(await resolve(request()), null);
});

test("request-controlled identity fields cannot override verified session", async () => {
  const resolve = createProductionAuthenticatedAccountResolver({
    verifySession: async () => ({ accountId: "acct-trusted-3" })
  });

  const attackerRequest = new Request("https://identity.invalid/token?accountId=acct-attacker&productId=attacker-product", {
    method: "POST",
    headers: {
      "content-type": "application/json",
      "x-account-id": "acct-attacker",
      "x-product-id": "attacker-product"
    },
    body: JSON.stringify({
      accountId: "acct-attacker",
      productId: "attacker-product"
    })
  });

  assert.deepEqual(await resolve(attackerRequest), {
    accountId: "acct-trusted-3",
    productId: "pcspa-pro"
  });
});

test("invalid verified session fails closed", async () => {
  const badShape = createProductionAuthenticatedAccountResolver({
    verifySession: async () => "acct-not-an-object"
  });

  await assert.rejects(() => badShape(request()), /verified session must be an object/);

  const missingAccount = createProductionAuthenticatedAccountResolver({
    verifySession: async () => ({ productId: "pcspa-pro" })
  });

  await assert.rejects(() => missingAccount(request()), /session.accountId/);
});

test("adapter validates dependencies and trusted product", () => {
  assert.throws(
    () => createProductionAuthenticatedAccountResolver(),
    /verifySession must be a function/
  );

  assert.throws(
    () => createProductionAuthenticatedAccountResolver({
      verifySession: async () => null,
      productId: ""
    }),
    /productId/
  );
});
