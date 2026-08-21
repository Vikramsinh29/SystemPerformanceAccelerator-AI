import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const migrationUrl =
  new URL("../migrations/0003_commercial_billing_core.sql", import.meta.url);

const sql = await readFile(migrationUrl, "utf8");

const requiredTables = [
  "billing_customers",
  "billing_plans",
  "billing_prices",
  "billing_subscriptions",
  "billing_transactions",
  "billing_provider_events",
  "billing_provider_mappings",
  "billing_ledger_entries"
];

test("commercial billing migration defines the provider-neutral C1 tables", () => {
  for (const table of requiredTables) {
    assert.match(
      sql,
      new RegExp(`CREATE TABLE ${table}\\s*\\(`),
      `missing table ${table}`
    );
  }
});

test("money is represented in integer minor units", () => {
  assert.match(sql, /unit_amount_minor INTEGER NOT NULL/);
  assert.match(sql, /gross_amount_minor INTEGER NOT NULL/);
  assert.match(sql, /processor_fee_minor INTEGER NOT NULL/);
  assert.match(sql, /net_receivable_minor INTEGER NOT NULL/);
  assert.match(sql, /amount_minor INTEGER NOT NULL/);

  assert.doesNotMatch(sql, /\bREAL\b/i);
  assert.doesNotMatch(sql, /\bFLOAT\b/i);
  assert.doesNotMatch(sql, /\bDOUBLE\b/i);
});

test("core commercial tables do not contain provider-specific columns", () => {
  const forbidden = [
    "paddle_",
    "razorpay_",
    "cashfree_",
    "stripe_",
    "paypal_"
  ];

  for (const value of forbidden) {
    assert.doesNotMatch(sql, new RegExp(value, "i"));
  }
});

test("provider references are isolated behind generic mappings", () => {
  assert.match(
    sql,
    /CREATE TABLE billing_provider_mappings/
  );

  assert.match(
    sql,
    /UNIQUE \(provider, provider_ref\)/
  );

  assert.match(
    sql,
    /UNIQUE \(provider, entity_type, internal_entity_id\)/
  );
});

test("provider event ingestion is idempotent", () => {
  assert.match(
    sql,
    /PRIMARY KEY \(provider, provider_event_id\)/
  );

  assert.match(
    sql,
    /payload_sha256 TEXT NOT NULL/
  );

  assert.match(
    sql,
    /attempt_count INTEGER NOT NULL DEFAULT 1/
  );
});

test("subscription lifecycle supports billing and grace transitions", () => {
  for (const state of [
    "pending",
    "active",
    "past_due",
    "grace",
    "canceled",
    "expired",
    "suspended"
  ]) {
    assert.match(sql, new RegExp(`'${state}'`));
  }

  assert.match(sql, /period_ends_utc TEXT/);
  assert.match(sql, /payment_grace_ends_utc TEXT/);
  assert.match(sql, /cancel_at_period_end INTEGER/);
});

test("financial ledger records economic components independently", () => {
  for (const entryType of [
    "tax",
    "gross",
    "processor_fee",
    "processor_fee_tax",
    "refund",
    "chargeback",
    "net_receivable"
  ]) {
    assert.match(sql, new RegExp(`'${entryType}'`));
  }
});

test("billing migration does not modify Licensing V2 entitlement tables", () => {
  assert.doesNotMatch(
    sql,
    /ALTER\s+TABLE\s+licensing_entitlements/i
  );

  assert.doesNotMatch(
    sql,
    /DROP\s+TABLE\s+licensing_/i
  );
});
test("billing subscriptions persist provider event ordering time", () => {
  assert.match(
    sql,
    /last_provider_event_utc TEXT/
  );
});