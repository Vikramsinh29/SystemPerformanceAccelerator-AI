import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import productionWorker from "../src/production-entrypoint.js";

const productionConfigUrl = new URL("../wrangler.production.jsonc", import.meta.url);
const productionEntrypointUrl = new URL("../src/production-entrypoint.js", import.meta.url);

test("production entrypoint remains fail-closed until explicit enablement", async () => {
  const response = await productionWorker.fetch(new Request("https://production.invalid/account/license"), {});
  assert.equal(response.status, 503);
  assert.deepEqual(await response.json(), {
    error: "production_not_enabled",
    message: "Licensing V2 production runtime is configured but not enabled."
  });
  assert.equal(response.headers.get("cache-control"), "no-store");
});

test("production config binds only the verified production D1 and no custom route", async () => {
  const config = await readFile(productionConfigUrl, "utf8");
  assert.match(config, /"name"\s*:\s*"pc-spa-licensing-v2-production"/);
  assert.match(config, /"main"\s*:\s*"src\/production-entrypoint\.js"/);
  assert.match(config, /"binding"\s*:\s*"LICENSING_DB"/);
  assert.match(config, /"database_name"\s*:\s*"pc-spa"/);
  assert.match(config, /"database_id"\s*:\s*"ff7e024c-0b2e-462f-83d8-07cc5d41612b"/);
  assert.doesNotMatch(config, /723d1e78-388e-4aac-88f8-e22fdfab0c41/);
  assert.doesNotMatch(config, /pc-spa-licensing-v2-staging/);
  assert.doesNotMatch(config, /STAGING_ACCESS_TOKEN/);
  assert.doesNotMatch(config, /STAGING_ACCOUNT_ID/);
  assert.doesNotMatch(config, /PRODUCTION_LICENSING_ENABLED/);
  assert.doesNotMatch(config, /LICENSING_IDENTITY_SECRET/);
  assert.doesNotMatch(config, /pc-spa-web/);
  assert.doesNotMatch(config, /getpcspa\.com/);
  assert.doesNotMatch(config, /"routes?"\s*:/);
});

test("production entrypoint requires explicit gate and contains no staging wiring", async () => {
  const source = await readFile(productionEntrypointUrl, "utf8");
  assert.match(source, /PRODUCTION_LICENSING_ENABLED/);
  assert.match(source, /production_not_enabled/);
  assert.match(source, /json\(503,/);
  assert.match(source, /createProductionLicensingRuntime/);
  assert.match(source, /createProductionLicensingRouter/);
  assert.doesNotMatch(source, /createLicensingIdentityBridge/);
  assert.doesNotMatch(source, /createLicensingStagingRouter/);
  assert.doesNotMatch(source, /STAGING_ACCESS_TOKEN/);
  assert.doesNotMatch(source, /getpcspa\.com/);
});
