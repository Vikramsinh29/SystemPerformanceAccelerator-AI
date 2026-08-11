import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import stagingWorker from "../src/staging-entrypoint.js";

const validDb = {
  prepare() {
    return { bind() { return this; }, first: async () => null, run: async () => ({ success: true }) };
  },
  async batch() { return []; }
};

test("staging entrypoint rejects requests without staging access token", async () => {
  const response = await stagingWorker.fetch(
    new Request("https://staging.example/account/license"),
    {}
  );
  assert.equal(response.status, 401);
  assert.deepEqual(await response.json(), { error: "staging_unauthorized" });
});

test("authorized staging request uses isolated runtime and router", async () => {
  const token = "1234567890abcdef";
  const response = await stagingWorker.fetch(
    new Request("https://staging.example/unknown", {
      headers: { "x-pcspa-staging-token": token }
    }),
    {
      STAGING_ACCESS_TOKEN: token,
      STAGING_ACCOUNT_ID: "staging-account",
      LICENSING_DB: validDb
    }
  );
  assert.equal(response.status, 404);
  assert.deepEqual(await response.json(), { error: "not_found" });
});

test("staging config uses a separate Worker and staging D1 placeholder", async () => {
  const configUrl = new URL("../wrangler.staging.jsonc", import.meta.url);
  const config = JSON.parse(await readFile(configUrl, "utf8"));
  assert.equal(config.name, "pc-spa-licensing-v2-staging");
  assert.equal(config.main, "src/staging-entrypoint.js");
  assert.equal(config.d1_databases[0].binding, "LICENSING_DB");
  assert.equal(config.d1_databases[0].database_name, "pc-spa-licensing-v2-staging");
  assert.equal(config.d1_databases[0].database_id, "00000000-0000-0000-0000-000000000000");
});

test("staging config has no production custom route or production Worker name", async () => {
  const configUrl = new URL("../wrangler.staging.jsonc", import.meta.url);
  const text = await readFile(configUrl, "utf8");
  assert.doesNotMatch(text, /getpcspa\.com/i);
  assert.doesNotMatch(text, /pc-spa-web/i);
  assert.doesNotMatch(text, /"routes"\s*:/i);
  assert.doesNotMatch(text, /"route"\s*:/i);
});

test("staging account identity is fixed by environment rather than request data", async () => {
  const sourceUrl = new URL("../src/staging-entrypoint.js", import.meta.url);
  const source = await readFile(sourceUrl, "utf8");
  assert.match(source, /STAGING_ACCOUNT_ID/);
  assert.match(source, /resolveAuthenticatedAccount:\s*async \(\) =>/);
  assert.doesNotMatch(source, /accountId\s*:\s*request/i);
});
