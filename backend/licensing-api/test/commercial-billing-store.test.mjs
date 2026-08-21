import assert from "node:assert/strict";
import test from "node:test";

import {
  CommercialBillingStore
} from "../src/commercial-billing-store.js";

class Statement {
  constructor(db, sql) {
    this.db = db;
    this.sql = sql;
    this.values = [];
  }

  bind(...values) {
    this.values = values;
    return this;
  }

  async first() {
    this.db.firstCalls.push({
      sql: this.sql,
      values: this.values
    });

    return this.db.firstResult ?? null;
  }

  async run() {
    this.db.runCalls.push({
      sql: this.sql,
      values: this.values
    });

    return this.db.runResults.shift() ?? {
      meta: { changes: 1 }
    };
  }
}

class FakeDb {
  constructor() {
    this.firstCalls = [];
    this.runCalls = [];
    this.batchCalls = [];
    this.runResults = [];
    this.batchResults = [];
    this.firstResult = null;
  }

  prepare(sql) {
    return new Statement(this, sql);
  }

  async batch(statements) {
    this.batchCalls.push(statements);

    if (this.batchResults.length > 0) {
      return this.batchResults.shift();
    }

    return statements.map(() => ({
      meta: { changes: 1 }
    }));
  }
}

function providerEvent() {
  return {
    provider: "ExampleProvider",
    providerEventId: "evt-1",
    eventType: "subscription.paid",
    occurredUtc: "2026-08-21T10:00:00Z",
    payloadSha256:
      "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
  };
}

function subscription(overrides = {}) {
  return {
    subscriptionId: "sub-1",
    customerId: "cus-1",
    productId: "pcspa-pro",
    planId: "plan-1",
    priceId: "price-1",
    state: "active",
    periodStartsUtc: "2026-08-21T00:00:00Z",
    periodEndsUtc: "2027-08-21T00:00:00Z",
    paymentGraceEndsUtc: null,
    lastProviderEventUtc: "2026-08-21T10:00:00Z",
    cancelAtPeriodEnd: false,
    canceledUtc: null,
    ...overrides
  };
}

function transaction() {
  return {
    transactionId: "txn-1",
    customerId: "cus-1",
    subscriptionId: "sub-1",
    transactionKind: "charge",
    status: "paid",
    currency: "USD",
    listAmountMinor: 2999,
    discountAmountMinor: 0,
    subtotalMinor: 2999,
    taxAmountMinor: 0,
    grossAmountMinor: 2999,
    processorFeeMinor: 200,
    processorFeeTaxMinor: 0,
    refundAmountMinor: 0,
    chargebackAmountMinor: 0,
    netReceivableMinor: 2799,
    occurredUtc: "2026-08-21T10:00:00Z",
    settledUtc: null
  };
}

function ledgerEntries() {
  return [
    {
      ledgerEntryId: "led-1",
      transactionId: "txn-1",
      entryType: "gross",
      currency: "USD",
      amountMinor: 2999,
      occurredUtc: "2026-08-21T10:00:00Z"
    },
    {
      ledgerEntryId: "led-2",
      transactionId: "txn-1",
      entryType: "processor_fee",
      currency: "USD",
      amountMinor: -200,
      occurredUtc: "2026-08-21T10:00:00Z"
    },
    {
      ledgerEntryId: "led-3",
      transactionId: "txn-1",
      entryType: "net_receivable",
      currency: "USD",
      amountMinor: 2799,
      occurredUtc: "2026-08-21T10:00:00Z"
    }
  ];
}

test("provider event receipt is idempotent by provider and event id", async () => {
  const db = new FakeDb();
  const store = new CommercialBillingStore(db);

  db.runResults.push(
    { meta: { changes: 1 } },
    { meta: { changes: 0 } }
  );

  const first = await store.recordProviderEvent(
    providerEvent(),
    "2026-08-21T10:00:01Z"
  );

  const second = await store.recordProviderEvent(
    providerEvent(),
    "2026-08-21T10:00:02Z"
  );

  assert.deepEqual(first, { duplicate: false });
  assert.deepEqual(second, { duplicate: true });

  assert.match(
    db.runCalls[0].sql,
    /INSERT OR IGNORE INTO billing_provider_events/
  );
});

test("processed provider event cannot be rewritten by retryable failure path", async () => {
  const db = new FakeDb();
  const store = new CommercialBillingStore(db);

  await store.markProviderEventRetryableFailure({
    provider: "exampleprovider",
    providerEventId: "evt-1",
    errorCode: "temporary_failure",
    attemptedUtc: "2026-08-21T10:05:00Z"
  });

  assert.match(
    db.runCalls[0].sql,
    /processing_status <> 'processed'/
  );

  assert.match(
    db.runCalls[0].sql,
    /attempt_count = attempt_count \+ 1/
  );
});

test("provider mappings remain generic and update by internal identity", async () => {
  const db = new FakeDb();
  const store = new CommercialBillingStore(db);

  const result = await store.upsertProviderMapping({
    mappingId: "map-1",
    provider: "ExampleProvider",
    entityType: "subscription",
    internalEntityId: "sub-1",
    providerRef: "external-sub-99",
    createdUtc: "2026-08-21T10:00:00Z",
    updatedUtc: "2026-08-21T10:00:00Z"
  });

  assert.deepEqual(result, { changed: true });

  assert.match(
    db.runCalls[0].sql,
    /ON CONFLICT\(provider, entity_type, internal_entity_id\)/
  );

  assert.equal(
    db.runCalls[0].values[1],
    "exampleprovider"
  );
});

test("new subscription transition writes subscription transaction and ledger atomically", async () => {
  const db = new FakeDb();
  const store = new CommercialBillingStore(db);

  const result = await store.commitSubscriptionTransition({
    previousVersion: -1,
    subscription: subscription(),
    transaction: transaction(),
    ledgerEntries: ledgerEntries(),
    nowUtc: "2026-08-21T10:00:00Z"
  });

  assert.equal(db.batchCalls.length, 1);
  assert.equal(db.batchCalls[0].length, 5);

  assert.deepEqual(result, {
    subscriptionChanged: true,
    transactionWritten: true,
    ledgerEntriesWritten: 3
  });

  assert.match(
    db.batchCalls[0][0].sql,
    /INSERT INTO billing_subscriptions/
  );

  assert.match(
    db.batchCalls[0][1].sql,
    /INSERT INTO billing_transactions/
  );
});

test("existing subscription transition uses optimistic version guard", async () => {
  const db = new FakeDb();
  const store = new CommercialBillingStore(db);

  await store.commitSubscriptionTransition({
    previousVersion: 4,
    subscription: subscription({
      state: "grace",
      paymentGraceEndsUtc: "2026-08-28T00:00:00Z"
    }),
    transaction: transaction(),
    ledgerEntries: ledgerEntries(),
    nowUtc: "2026-08-21T10:00:00Z"
  });

  const subscriptionStatement = db.batchCalls[0][0];

  assert.match(
    subscriptionStatement.sql,
    /WHERE subscription_id = \?\s+AND version = \?/
  );

  assert.match(
    subscriptionStatement.sql,
    /version = version \+ 1/
  );

  assert.equal(
    subscriptionStatement.values.at(-1),
    4
  );
});

test("failed optimistic subscription write is surfaced independently from financial writes", async () => {
  const db = new FakeDb();
  const store = new CommercialBillingStore(db);

  db.batchResults.push([
    { meta: { changes: 0 } },
    { meta: { changes: 1 } },
    { meta: { changes: 1 } },
    { meta: { changes: 1 } },
    { meta: { changes: 1 } }
  ]);

  const result = await store.commitSubscriptionTransition({
    previousVersion: 7,
    subscription: subscription(),
    transaction: transaction(),
    ledgerEntries: ledgerEntries(),
    nowUtc: "2026-08-21T10:00:00Z"
  });

  assert.deepEqual(result, {
    subscriptionChanged: false,
    transactionWritten: true,
    ledgerEntriesWritten: 3
  });
});

test("ledger entries must belong to the committed transaction", async () => {
  const db = new FakeDb();
  const store = new CommercialBillingStore(db);

  const invalid = ledgerEntries();
  invalid[0] = {
    ...invalid[0],
    transactionId: "txn-other"
  };

  await assert.rejects(
    () => store.commitSubscriptionTransition({
      previousVersion: -1,
      subscription: subscription(),
      transaction: transaction(),
      ledgerEntries: invalid,
      nowUtc: "2026-08-21T10:00:00Z"
    }),
    /transactionId mismatch/
  );

  assert.equal(db.batchCalls.length, 0);
});

test("store refuses invalid database bindings", () => {
  assert.throws(
    () => new CommercialBillingStore(null),
    /D1-compatible database binding/
  );

  assert.throws(
    () => new CommercialBillingStore({}),
    /D1-compatible database binding/
  );
});
test("subscription persistence carries provider event ordering timestamp", async () => {
  const db = new FakeDb();
  const store = new CommercialBillingStore(db);

  await store.commitSubscriptionTransition({
    previousVersion: 3,
    subscription: subscription({
      lastProviderEventUtc: "2026-08-21T10:30:00Z"
    }),
    transaction: transaction(),
    ledgerEntries: ledgerEntries(),
    nowUtc: "2026-08-21T10:30:01Z"
  });

  const statement = db.batchCalls[0][0];

  assert.match(
    statement.sql,
    /last_provider_event_utc = \?/
  );

  assert.ok(
    statement.values.includes("2026-08-21T10:30:00Z")
  );
});