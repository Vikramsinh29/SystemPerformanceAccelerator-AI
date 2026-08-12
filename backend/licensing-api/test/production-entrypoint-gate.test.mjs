import assert from "node:assert/strict";
import test from "node:test";
import productionWorker from "../src/production-entrypoint.js";

const strongSecret = "production-entrypoint-gate-secret-0123456789abcdef";

function fakeDatabase() {
  return {
    prepare() {
      return {
        bind() { return this; },
        first: async () => null,
        run: async () => ({ success: true })
      };
    },
    batch: async () => []
  };
}

function request(path = "/account/license", headers = {}) {
  return new Request(`https://production.invalid${path}`, { headers });
}

test("production entrypoint defaults closed when enable flag is absent", async () => {
  const response = await productionWorker.fetch(request(), {
    LICENSING_DB: fakeDatabase(),
    LICENSING_IDENTITY_SECRET: strongSecret
  });

  assert.equal(response.status, 503);
  assert.deepEqual(await response.json(), {
    error: "production_not_enabled",
    message: "Licensing V2 production runtime is configured but not enabled."
  });
});

test("production entrypoint requires exact enable flag value", async () => {
  for (const value of ["true", "1", "ENABLED", " enabled "]) {
    const response = await productionWorker.fetch(request(), {
      PRODUCTION_LICENSING_ENABLED: value,
      LICENSING_DB: fakeDatabase(),
      LICENSING_IDENTITY_SECRET: strongSecret
    });

    assert.equal(response.status, 503);
    assert.equal((await response.json()).error, "production_not_enabled");
  }
});

test("enabled production entrypoint fails closed without a strong identity secret", async () => {
  const response = await productionWorker.fetch(request(), {
    PRODUCTION_LICENSING_ENABLED: "enabled",
    LICENSING_DB: fakeDatabase(),
    LICENSING_IDENTITY_SECRET: "too-short"
  });

  assert.equal(response.status, 503);
  assert.deepEqual(await response.json(), {
    error: "production_not_ready",
    message: "Licensing V2 production runtime is not ready."
  });
});

test("enabled production entrypoint reaches authenticated runtime only when fully configured", async () => {
  const response = await productionWorker.fetch(request(), {
    PRODUCTION_LICENSING_ENABLED: "enabled",
    LICENSING_DB: fakeDatabase(),
    LICENSING_IDENTITY_SECRET: strongSecret
  });

  assert.equal(response.status, 401);
  assert.deepEqual(await response.json(), { error: "unauthenticated" });
});

test("enabled production entrypoint exposes only the bounded production router", async () => {
  const response = await productionWorker.fetch(request("/unknown"), {
    PRODUCTION_LICENSING_ENABLED: "enabled",
    LICENSING_DB: fakeDatabase(),
    LICENSING_IDENTITY_SECRET: strongSecret
  });

  assert.equal(response.status, 404);
  assert.deepEqual(await response.json(), { error: "not_found" });
});
