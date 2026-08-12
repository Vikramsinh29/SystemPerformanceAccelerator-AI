import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import {
  LICENSING_RUNTIME_OPERATIONS
} from "../src/licensing-worker-runtime.js";
import { createProductionLicensingRuntime } from "../src/production-licensing-runtime.js";
import { createProductionLicensingRouter } from "../src/production-licensing-router.js";

const strongSecret = "production-runtime-test-secret-0123456789abcdef";

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

test("production runtime maps missing production identity to stable 401", async () => {
  const runtime = createProductionLicensingRuntime({
    env: { LICENSING_DB: fakeDatabase() },
    identitySecret: strongSecret
  });

  const response = await runtime.handle(
    LICENSING_RUNTIME_OPERATIONS.READ_ACCOUNT_LICENSE,
    new Request("https://production.invalid/account/license")
  );

  assert.equal(response.status, 401);
  assert.deepEqual(await response.json(), { error: "unauthenticated" });
});

test("production runtime refuses weak identity secret", () => {
  assert.throws(
    () => createProductionLicensingRuntime({
      env: { LICENSING_DB: fakeDatabase() },
      identitySecret: "too-short"
    }),
    /at least 32 characters/
  );
});

test("production router maps only approved licensing operations", async () => {
  const calls = [];
  const runtime = {
    async handle(operation) {
      calls.push(operation);
      return new Response(null, { status: 204 });
    }
  };
  const router = createProductionLicensingRouter({ runtime });

  const expected = [
    ["/account/license", LICENSING_RUNTIME_OPERATIONS.READ_ACCOUNT_LICENSE],
    ["/activate", LICENSING_RUNTIME_OPERATIONS.ACTIVATE_DEVICE],
    ["/deactivate", LICENSING_RUNTIME_OPERATIONS.DEACTIVATE_DEVICE],
    ["/validate", LICENSING_RUNTIME_OPERATIONS.VALIDATE_DEVICE]
  ];

  for (const [path, operation] of expected) {
    calls.length = 0;
    const response = await router.fetch(new Request(`https://production.invalid${path}`));
    assert.equal(response.status, 204);
    assert.deepEqual(calls, [operation]);
  }

  calls.length = 0;
  const unknown = await router.fetch(new Request("https://production.invalid/unknown"));
  assert.equal(unknown.status, 404);
  assert.deepEqual(await unknown.json(), { error: "not_found" });
  assert.equal(calls.length, 0);
});

test("production entrypoint remains fail-closed and does not instantiate internal runtime", async () => {
  const entryUrl = new URL("../src/production-entrypoint.js", import.meta.url);
  const text = await readFile(fileURLToPath(entryUrl), "utf8");

  assert.match(text, /production_not_enabled/);
  assert.match(text, /status:\s*503/);
  assert.doesNotMatch(text, /createProductionLicensingRuntime/);
  assert.doesNotMatch(text, /createProductionLicensingRouter/);
});
