import assert from "node:assert/strict";
import test from "node:test";
import { D1DeviceActivationStore } from "../src/device-activation-store.js";

class Statement {
  constructor(db, sql) { this.db = db; this.sql = sql; this.values = []; }
  bind(...values) { this.values = values; return this; }
  async first() { return this.db.firstResult; }
  async all() { return this.db.allResult; }
  async run() { this.db.runs.push(this); return this.db.nextRun ?? { meta: { changes: 1 } }; }
}

class FakeD1 {
  constructor() {
    this.runs = [];
    this.batches = [];
    this.firstResult = null;
    this.allResult = { results: [] };
    this.nextRun = null;
    this.nextBatch = null;
  }
  prepare(sql) { return new Statement(this, sql); }
  async batch(statements) {
    this.batches.push(statements);
    return this.nextBatch ?? statements.map(() => ({ meta: { changes: 1 } }));
  }
}

const device = {
  activationId: "act-1",
  entitlementId: "ent-1",
  accountId: "acct-1",
  productId: "pcspa-pro",
  deviceFingerprintHash: "a".repeat(64),
  deviceLabel: "Primary PC"
};
const nowUtc = "2026-08-11T17:45:00Z";

test("activation is gated by entitlement state, seat capacity, and active fingerprint uniqueness", async () => {
  const db = new FakeD1();
  const result = await new D1DeviceActivationStore(db).activateDevice(device, nowUtc);

  assert.equal(db.batches.length, 1);
  assert.equal(db.batches[0].length, 2);
  assert.match(db.batches[0][0].sql, /state IN \('active', 'grace'\)/);
  assert.match(db.batches[0][0].sql, /active_device_count < seat_limit/);
  assert.match(db.batches[0][0].sql, /NOT EXISTS/);
  assert.match(db.batches[0][1].sql, /SELECT COUNT\(\*\).*status = 'active'/s);
  assert.deepEqual(result, { activated: true, entitlementReconciled: true });
});

test("seat denial or duplicate activation reports no activation", async () => {
  const db = new FakeD1();
  db.nextBatch = [{ meta: { changes: 0 } }, { meta: { changes: 0 } }];

  const result = await new D1DeviceActivationStore(db).activateDevice(device, nowUtc);
  assert.deepEqual(result, { activated: false, entitlementReconciled: false });
});

test("deactivation uses optimistic version guard and reconciles entitlement count", async () => {
  const db = new FakeD1();
  await new D1DeviceActivationStore(db).deactivateDevice("act-1", 3, nowUtc);

  assert.equal(db.batches.length, 1);
  assert.equal(db.batches[0].length, 2);
  assert.match(db.batches[0][0].sql, /status = \?, deactivated_utc = \?/);
  assert.match(db.batches[0][0].sql, /status = 'active' AND version = \?/);
  assert.deepEqual(db.batches[0][0].values, ["deactivated", nowUtc, "act-1", 3]);
  assert.match(db.batches[0][1].sql, /active_device_count =/);
});

test("revocation records revoked timestamp", async () => {
  const db = new FakeD1();
  await new D1DeviceActivationStore(db).revokeDevice("act-1", 1, nowUtc);
  assert.match(db.batches[0][0].sql, /revoked_utc/);
  assert.deepEqual(db.batches[0][0].values, ["revoked", nowUtc, "act-1", 1]);
});

test("validation touch is active-only and version guarded", async () => {
  const db = new FakeD1();
  await new D1DeviceActivationStore(db).touchValidation("act-1", 7, nowUtc);

  assert.equal(db.runs.length, 1);
  assert.match(db.runs[0].sql, /last_validated_utc = \?, version = version \+ 1/);
  assert.match(db.runs[0].sql, /status = 'active' AND version = \?/);
  assert.deepEqual(db.runs[0].values, [nowUtc, "act-1", 7]);
});

test("raw or malformed device fingerprints are rejected before database access", async () => {
  const db = new FakeD1();
  await assert.rejects(
    () => new D1DeviceActivationStore(db).activateDevice({
      ...device,
      deviceFingerprintHash: "raw-machine-id"
    }, nowUtc),
    /SHA-256/
  );
  assert.equal(db.batches.length, 0);
});

test("findActiveDevice requires a hashed fingerprint", async () => {
  const db = new FakeD1();
  db.firstResult = { activation_id: "act-1", status: "active" };

  const result = await new D1DeviceActivationStore(db).findActiveDevice(
    "ent-1",
    "b".repeat(64)
  );

  assert.deepEqual(result, db.firstResult);
});
