import test from "node:test";
import assert from "node:assert/strict";

import {
  createLicensingStagingRouter,
  LICENSING_RUNTIME_OPERATIONS
} from "../src/index.js";

test("staging account-license route delegates to authenticated runtime", async () => {
  const calls = [];
  const runtime = {
    async handle(operation, request) {
      calls.push({ operation, request });
      return new Response(JSON.stringify({ ok: true }), { status: 200 });
    }
  };

  const router = createLicensingStagingRouter({ runtime });
  const request = new Request("https://staging.example/account/license", { method: "GET" });
  const response = await router.fetch(request);

  assert.equal(response.status, 200);
  assert.equal(calls.length, 1);
  assert.equal(calls[0].operation, LICENSING_RUNTIME_OPERATIONS.READ_ACCOUNT_LICENSE);
  assert.equal(calls[0].request, request);
});

test("staging device routes map only to existing runtime operations", async () => {
  const seen = [];
  const runtime = {
    async handle(operation) {
      seen.push(operation);
      return new Response(null, { status: 204 });
    }
  };

  const router = createLicensingStagingRouter({ runtime });

  for (const [path, expected] of [
    ["/activate", LICENSING_RUNTIME_OPERATIONS.ACTIVATE_DEVICE],
    ["/deactivate", LICENSING_RUNTIME_OPERATIONS.DEACTIVATE_DEVICE],
    ["/validate", LICENSING_RUNTIME_OPERATIONS.VALIDATE_DEVICE]
  ]) {
    const response = await router.fetch(new Request(`https://staging.example${path}`, { method: "POST" }));
    assert.equal(response.status, 204);
    assert.equal(seen.at(-1), expected);
  }
});

test("unknown staging path returns 404 without runtime execution", async () => {
  let calls = 0;
  const runtime = {
    async handle() {
      calls += 1;
      return new Response(null, { status: 204 });
    }
  };

  const router = createLicensingStagingRouter({ runtime });
  const response = await router.fetch(new Request("https://staging.example/admin/licenses"));

  assert.equal(response.status, 404);
  assert.deepEqual(await response.json(), { error: "not_found" });
  assert.equal(calls, 0);
});

test("invalid request returns 400 without runtime execution", async () => {
  let calls = 0;
  const router = createLicensingStagingRouter({
    runtime: {
      async handle() {
        calls += 1;
        return new Response(null, { status: 204 });
      }
    }
  });

  const response = await router.fetch({ url: "not a valid absolute url" });

  assert.equal(response.status, 400);
  assert.deepEqual(await response.json(), { error: "invalid_request" });
  assert.equal(calls, 0);
});

test("staging router requires a prebuilt runtime", () => {
  assert.throws(
    () => createLicensingStagingRouter(),
    /runtime\.handle is required/
  );
});
