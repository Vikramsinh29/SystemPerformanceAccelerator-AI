import test from "node:test";
import assert from "node:assert/strict";

import {
  LICENSING_RUNTIME_OPERATIONS,
  createLicensingWorkerRuntime
} from "../src/licensing-worker-runtime.js";

function fakeDatabase({ firstResult = null } = {}) {
  const statement = {
    bind() {
      return this;
    },
    first: async () => firstResult,
    run: async () => ({ success: true })
  };

  return {
    prepare() {
      return statement;
    },
    async batch() {
      return [];
    }
  };
}

test("runtime composes the production D1 binding and authenticated identity", async () => {
  const database = fakeDatabase({
    firstResult: {
      entitlement_id: "ent-1",
      account_id: "acct-1",
      product_id: "pcspa-pro",
      state: "active",
      seat_limit: 3,
      active_device_count: 1,
      period_ends_utc: "2026-09-01T00:00:00.000Z",
      payment_grace_ends_utc: null,
      offline_valid_until_utc: null,
      version: 0
    }
  });

  const runtime = createLicensingWorkerRuntime({
    env: { LICENSING_DB: database },
    resolveAuthenticatedAccount: async () => ({ accountId: "acct-1" }),
    clock: () => "2026-08-12T00:00:00.000Z"
  });

  assert.equal(runtime.composition.eventStore.database, database);
  assert.equal(runtime.composition.deviceStore.database, database);

  const response = await runtime.handle(
    LICENSING_RUNTIME_OPERATIONS.READ_ACCOUNT_LICENSE,
    new Request("https://internal.invalid/license", { method: "GET" })
  );

  assert.equal(response.status, 200);
  const body = await response.json();
  assert.equal(body.license.accountId, "acct-1");
  assert.equal(body.license.productId, "pcspa-pro");
});

test("unauthenticated identity is rejected before licensing handler execution", async () => {
  const runtime = createLicensingWorkerRuntime({
    env: { LICENSING_DB: fakeDatabase() },
    resolveAuthenticatedAccount: async () => null
  });

  const response = await runtime.handle(
    LICENSING_RUNTIME_OPERATIONS.READ_ACCOUNT_LICENSE,
    new Request("https://internal.invalid/license", { method: "GET" })
  );

  assert.equal(response.status, 401);
  assert.deepEqual(await response.json(), { error: "unauthenticated" });
});

test("request-controlled identity remains outside the runtime trust boundary", async () => {
  const runtime = createLicensingWorkerRuntime({
    env: { LICENSING_DB: fakeDatabase() },
    resolveAuthenticatedAccount: async () => null
  });

  const response = await runtime.handle(
    LICENSING_RUNTIME_OPERATIONS.ACTIVATE_DEVICE,
    new Request("https://internal.invalid/activate", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        accountId: "attacker-account",
        productId: "attacker-product",
        deviceFingerprintHash: "a".repeat(64)
      })
    })
  );

  assert.equal(response.status, 401);
});

test("runtime refuses missing or invalid LICENSING_DB binding", () => {
  assert.throws(
    () => createLicensingWorkerRuntime({
      env: {},
      resolveAuthenticatedAccount: async () => ({ accountId: "acct-1" })
    }),
    /env\.LICENSING_DB/
  );
});

test("runtime rejects unsupported operations without exposing public routing", async () => {
  const runtime = createLicensingWorkerRuntime({
    env: { LICENSING_DB: fakeDatabase() },
    resolveAuthenticatedAccount: async () => ({ accountId: "acct-1" })
  });

  await assert.rejects(
    () => runtime.handle(
      "publicFetchRoute",
      new Request("https://internal.invalid/unknown", { method: "GET" })
    ),
    /Unsupported licensing runtime operation/
  );
});
