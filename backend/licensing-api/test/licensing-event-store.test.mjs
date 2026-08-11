import assert from "node:assert/strict";
import test from "node:test";
import { D1LicensingEventStore } from "../src/licensing-event-store.js";

class Statement {
  constructor(db, sql) { this.db = db; this.sql = sql; this.values = []; }
  bind(...values) { this.values = values; return this; }
  async first() { return this.db.firstResult; }
  async run() { this.db.runs.push(this); return { meta: { changes: 1 } }; }
}

class FakeD1 {
  constructor() { this.runs = []; this.batches = []; this.firstResult = null; }
  prepare(sql) { return new Statement(this, sql); }
  async batch(statements) {
    this.batches.push(statements);
    return this.nextBatch ?? statements.map(() => ({ meta: { changes: 1 } }));
  }
}

const event = {
  provider: "simulated", providerEventId: "evt-1", accountId: "acct-1",
  productId: "pcspa-pro", providerSubscriptionId: "sub-1",
  kind: "purchase_completed", occurredUtc: "2026-08-11T12:00:00Z"
};
const entitlement = {
  entitlementId: "ent-1", accountId: "acct-1", productId: "pcspa-pro",
  state: "active", seatLimit: 1, activeDeviceCount: 0,
  periodEndsUtc: "2027-08-11T12:00:00Z",
  paymentGraceEndsUtc: null, offlineValidUntilUtc: "2026-09-10T12:00:00Z",
  transfersUsed: 0, transferWindowStartedUtc: "2026-08-11T12:00:00Z",
  lastTransferUtc: null, lastCommercialEventUtc: "2026-08-11T12:00:00Z"
};
const audit = {
  auditId: "audit-1", occurredUtc: "2026-08-11T12:00:00Z",
  previousState: null, currentState: "active", message: "Activated."
};

test("new transition uses one atomic three-statement D1 batch", async () => {
  const db = new FakeD1();
  const store = new D1LicensingEventStore(db);
  const result = await store.commitTransition({
    event, previousVersion: -1, entitlement, audit, outcome: "applied",
    nowUtc: "2026-08-11T12:00:01Z"
  });
  assert.equal(db.batches.length, 1);
  assert.equal(db.batches[0].length, 3);
  assert.match(db.batches[0][0].sql, /ON CONFLICT\(provider, provider_event_id\) DO NOTHING/);
  assert.match(db.batches[0][1].sql, /ON CONFLICT\(account_id, product_id\) DO NOTHING/);
  assert.deepEqual(result, { duplicate: false, entitlementChanged: true, auditWritten: true });
});

test("existing transition uses optimistic version guard", async () => {
  const db = new FakeD1();
  const store = new D1LicensingEventStore(db);
  await store.commitTransition({
    event, previousVersion: 4, entitlement, audit, outcome: "applied",
    nowUtc: "2026-08-11T12:00:01Z"
  });
  assert.match(db.batches[0][1].sql, /version = version \+ 1/);
  assert.match(db.batches[0][1].sql, /AND version = \?/);
});

test("duplicate receipt is reported and dependent writes stay unchanged", async () => {
  const db = new FakeD1();
  db.nextBatch = [
    { meta: { changes: 0 } }, { meta: { changes: 0 } }, { meta: { changes: 0 } }
  ];
  const result = await new D1LicensingEventStore(db).commitTransition({
    event, previousVersion: -1, entitlement, audit, outcome: "applied",
    nowUtc: "2026-08-11T12:00:01Z"
  });
  assert.deepEqual(result, { duplicate: true, entitlementChanged: false, auditWritten: false });
});

test("retryable failure increments attempts but cannot overwrite processed receipt", async () => {
  const db = new FakeD1();
  await new D1LicensingEventStore(db).recordFailure(
    event, "temporary_database_failure", "2026-08-11T12:00:01Z");
  assert.equal(db.runs.length, 1);
  assert.match(db.runs[0].sql, /attempt_count = attempt_count \+ 1/);
  assert.match(db.runs[0].sql, /WHERE processing_status <> 'processed'/);
});

test("invalid state fails before any database call", async () => {
  const db = new FakeD1();
  await assert.rejects(() => new D1LicensingEventStore(db).commitTransition({
    event, previousVersion: -1,
    entitlement: { ...entitlement, state: "unknown" }, audit,
    outcome: "applied", nowUtc: "2026-08-11T12:00:01Z"
  }), /state is invalid/);
  assert.equal(db.batches.length, 0);
});
