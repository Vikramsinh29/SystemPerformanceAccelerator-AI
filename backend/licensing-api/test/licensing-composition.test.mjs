import assert from "node:assert/strict";
import test from "node:test";
import { createLicensingComposition } from "../src/licensing-composition.js";
import { D1LicensingEventStore } from "../src/licensing-event-store.js";
import { D1DeviceActivationStore } from "../src/device-activation-store.js";
import { LicensingCompatibilityService } from "../src/licensing-compatibility-service.js";

class Statement {
  constructor(db, sql) { this.db = db; this.sql = sql; this.values = []; }
  bind(...values) { this.values = values; return this; }
  async first() { return this.db.firstResult; }
  async all() { return { results: [] }; }
  async run() { return { meta: { changes: 1 } }; }
}

class FakeD1 {
  constructor() { this.firstResult = null; }
  prepare(sql) { return new Statement(this, sql); }
  async batch(statements) { return statements.map(() => ({ meta: { changes: 1 } })); }
}

test("composition wires one D1 binding through stores, service, and adapter", () => {
  const database = new FakeD1();
  const composition = createLicensingComposition({ database });

  assert.ok(composition.eventStore instanceof D1LicensingEventStore);
  assert.ok(composition.deviceStore instanceof D1DeviceActivationStore);
  assert.ok(composition.service instanceof LicensingCompatibilityService);
  assert.equal(composition.eventStore.database, database);
  assert.equal(composition.deviceStore.database, database);
  assert.equal(composition.service.eventStore, composition.eventStore);
  assert.equal(composition.service.deviceStore, composition.deviceStore);
  assert.equal(typeof composition.adapter.readAccountLicense, "function");
  assert.equal(typeof composition.adapter.activateDevice, "function");
  assert.equal(typeof composition.adapter.deactivateDevice, "function");
  assert.equal(typeof composition.adapter.validateDevice, "function");
  assert.equal(Object.isFrozen(composition), true);
});

test("composed adapter can execute an internal account-license read", async () => {
  const composition = createLicensingComposition({
    database: new FakeD1(),
    clock: () => "2026-08-11T18:10:00Z"
  });

  const response = await composition.adapter.readAccountLicense(
    new Request("https://internal.invalid/license", { method: "GET" }),
    { accountId: "acct-1", productId: "pcspa-pro" }
  );

  assert.equal(response.status, 404);
  assert.deepEqual(await response.json(), { error: "license_not_found" });
});

test("invalid database binding fails before composition is returned", () => {
  assert.throws(
    () => createLicensingComposition({ database: {} }),
    /D1-compatible database binding/
  );
});

test("invalid injected factories are rejected by their owning layer", () => {
  const database = new FakeD1();
  assert.throws(
    () => createLicensingComposition({ database, idFactory: "not-a-function" }),
    /idFactory must be a function/
  );
  assert.throws(
    () => createLicensingComposition({ database, clock: "not-a-function" }),
    /clock must be a function/
  );
});
