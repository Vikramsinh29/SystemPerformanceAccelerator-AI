import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import productionWorker from "../src/production-entrypoint.js";

const configUrl = new URL("../wrangler.production.jsonc", import.meta.url);

async function productionConfig() {
  return readFile(configUrl, "utf8");
}

test("production deployment rehearsal defaults to fail-closed worker", async () => {
  const response = await productionWorker.fetch(
    new Request("https://production.invalid/account/license"),
    {}
  );

  assert.equal(response.status, 503);
  assert.deepEqual(await response.json(), {
    error: "production_not_enabled",
    message: "Licensing V2 production runtime is configured but not enabled."
  });
  assert.equal(response.headers.get("cache-control"), "no-store");
});

test("production deployment config contains only isolated worker and verified D1 binding", async () => {
  const config = await productionConfig();

  assert.match(config, /"name"\s*:\s*"pc-spa-licensing-v2-production"/);
  assert.match(config, /"main"\s*:\s*"src\/production-entrypoint\.js"/);
  assert.match(config, /"workers_dev"\s*:\s*true/);
  assert.match(config, /"binding"\s*:\s*"LICENSING_DB"/);
  assert.match(config, /"database_name"\s*:\s*"pc-spa"/);
  assert.match(config, /"database_id"\s*:\s*"ff7e024c-0b2e-462f-83d8-07cc5d41612b"/);
});

test("production deployment config cannot activate licensing or claim production web route", async () => {
  const config = await productionConfig();

  for (const forbidden of [
    "PRODUCTION_LICENSING_ENABLED",
    "LICENSING_IDENTITY_SECRET",
    "getpcspa.com",
    "pc-spa-web",
    "STAGING_ACCESS_TOKEN",
    "STAGING_ACCOUNT_ID",
    "723d1e78-388e-4aac-88f8-e22fdfab0c41"
  ]) {
    assert.doesNotMatch(config, new RegExp(forbidden.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")));
  }

  assert.doesNotMatch(config, /"routes?"\s*:/);
});

test("rehearsal configuration does not persist production enable flag as a variable", async () => {
  const config = await productionConfig();
  assert.doesNotMatch(config, /"vars"\s*:/);
});
