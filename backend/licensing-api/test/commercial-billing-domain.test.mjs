import assert from "node:assert/strict";
import test from "node:test";

import {
  BillingDomainError,
  deriveEntitlementProjection,
  normalizeProviderEvent,
  transitionBillingState,
  validateMoneySnapshot,
  validatePlan
} from "../src/commercial-billing-domain.js";

test("billing state machine allows valid commercial transitions", () => {
  assert.deepEqual(
    transitionBillingState("pending", "active"),
    { changed: true, state: "active" }
  );

  assert.deepEqual(
    transitionBillingState("active", "past_due"),
    { changed: true, state: "past_due" }
  );

  assert.deepEqual(
    transitionBillingState("past_due", "grace"),
    { changed: true, state: "grace" }
  );

  assert.deepEqual(
    transitionBillingState("grace", "active"),
    { changed: true, state: "active" }
  );
});

test("billing state machine rejects impossible resurrection", () => {
  assert.throws(
    () => transitionBillingState("expired", "active"),
    (error) =>
      error instanceof BillingDomainError &&
      error.code === "invalid_state_transition"
  );
});

test("repeated same-state transition is idempotent", () => {
  assert.deepEqual(
    transitionBillingState("active", "active"),
    { changed: false, state: "active" }
  );
});

test("active subscription projects to active entitlement", () => {
  const projection = deriveEntitlementProjection({
    state: "active",
    periodEndsUtc: "2027-08-21T00:00:00Z",
    paymentGraceEndsUtc: null
  }, "2026-08-21T00:00:00Z");

  assert.deepEqual(projection, {
    state: "active",
    usable: true
  });
});

test("cancel-at-period-end behavior preserves purchased access", () => {
  const duringPaidPeriod = deriveEntitlementProjection({
    state: "canceled",
    periodEndsUtc: "2026-09-01T00:00:00Z",
    paymentGraceEndsUtc: null
  }, "2026-08-21T00:00:00Z");

  assert.deepEqual(duringPaidPeriod, {
    state: "active",
    usable: true
  });

  const afterPaidPeriod = deriveEntitlementProjection({
    state: "canceled",
    periodEndsUtc: "2026-09-01T00:00:00Z",
    paymentGraceEndsUtc: null
  }, "2026-09-02T00:00:00Z");

  assert.deepEqual(afterPaidPeriod, {
    state: "expired",
    usable: false
  });
});

test("past-due subscription maps into bounded grace access", () => {
  const projection = deriveEntitlementProjection({
    state: "past_due",
    periodEndsUtc: "2026-08-20T00:00:00Z",
    paymentGraceEndsUtc: "2026-08-28T00:00:00Z"
  }, "2026-08-21T00:00:00Z");

  assert.deepEqual(projection, {
    state: "grace",
    usable: true
  });
});

test("expired grace projects to expired entitlement", () => {
  const projection = deriveEntitlementProjection({
    state: "grace",
    periodEndsUtc: "2026-08-20T00:00:00Z",
    paymentGraceEndsUtc: "2026-08-22T00:00:00Z"
  }, "2026-08-23T00:00:00Z");

  assert.deepEqual(projection, {
    state: "expired",
    usable: false
  });
});

test("money snapshots require integer minor units", () => {
  const value = validateMoneySnapshot({
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
    netReceivableMinor: 2799
  });

  assert.equal(value.currency, "USD");

  assert.throws(
    () => validateMoneySnapshot({
      ...value,
      grossAmountMinor: 29.99
    }),
    /safe integer minor-unit amount/
  );
});

test("provider event normalization remains provider-neutral", () => {
  const event = normalizeProviderEvent({
    provider: "ExampleProvider",
    providerEventId: "evt-123",
    eventType: "subscription.paid",
    occurredUtc: "2026-08-21T10:00:00Z",
    payloadSha256:
      "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
  });

  assert.deepEqual(event, {
    provider: "exampleprovider",
    providerEventId: "evt-123",
    eventType: "subscription.paid",
    occurredUtc: "2026-08-21T10:00:00Z",
    payloadSha256:
      "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
  });
});

test("plan validation keeps commercial plans internal", () => {
  assert.deepEqual(
    validatePlan({
      planId: "plan-pro-annual-1",
      productId: "pcspa-pro",
      planCode: "PCSPA_PRO_ANNUAL",
      billingInterval: "annual",
      seatLimit: 1
    }),
    {
      planId: "plan-pro-annual-1",
      productId: "pcspa-pro",
      planCode: "PCSPA_PRO_ANNUAL",
      billingInterval: "annual",
      seatLimit: 1
    }
  );
});