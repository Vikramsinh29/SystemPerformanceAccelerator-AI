import assert from "node:assert/strict";
import test from "node:test";

import {
  CommercialEventOrchestrator,
  CommercialEventError
} from "../src/commercial-event-orchestrator.js";

function providerEvent(overrides = {}) {
  return {
    provider: "exampleprovider",
    providerEventId: "evt-100",
    eventType: "subscription.updated",
    occurredUtc: "2026-08-21T10:00:00Z",
    payloadSha256:
      "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    ...overrides
  };
}

function subscription(overrides = {}) {
  return {
    subscription_id: "sub-1",
    customer_id: "acct-1",
    product_id: "pcspa-pro",
    plan_id: "plan-pro",
    price_id: "price-usd",
    state: "active",
    period_starts_utc: "2026-08-01T00:00:00Z",
    period_ends_utc: "2027-08-01T00:00:00Z",
    payment_grace_ends_utc: null,
    last_provider_event_utc: "2026-08-20T10:00:00Z",
    cancel_at_period_end: 0,
    canceled_utc: null,
    version: 4,
    ...overrides
  };
}

function entitlement(overrides = {}) {
  return {
    entitlement_id: "ent-1",
    account_id: "acct-1",
    product_id: "pcspa-pro",
    state: "active",
    seat_limit: 1,
    active_device_count: 0,
    period_ends_utc: "2027-08-01T00:00:00Z",
    payment_grace_ends_utc: null,
    offline_valid_until_utc: null,
    transfers_used: 0,
    transfer_window_started_utc: "2026-08-01T00:00:00Z",
    last_transfer_utc: null,
    last_commercial_event_utc: "2026-08-20T10:00:00Z",
    version: 2,
    ...overrides
  };
}

class BillingStore {
  constructor() {
    this.subscription = subscription();
    this.providerEvent = null;
    this.transitionCalls = [];
    this.ignoredCalls = [];
    this.failureCalls = [];
  }

  async recordProviderEvent(event) {
    if (this.providerEvent) {
      return { duplicate: true };
    }

    this.providerEvent = {
      processing_status: "received",
      ...event
    };

    return { duplicate: false };
  }

  async findProviderEvent() {
    return this.providerEvent;
  }

  async findSubscription() {
    return this.subscription;
  }

  async commitProviderSubscriptionTransition(value) {
    this.transitionCalls.push(value);

    this.subscription = {
      ...this.subscription,
      state: value.subscription.state,
      period_starts_utc:
        value.subscription.periodStartsUtc,
      period_ends_utc:
        value.subscription.periodEndsUtc,
      payment_grace_ends_utc:
        value.subscription.paymentGraceEndsUtc,
      last_provider_event_utc:
        value.subscription.lastProviderEventUtc,
      cancel_at_period_end:
        value.subscription.cancelAtPeriodEnd ? 1 : 0,
      canceled_utc:
        value.subscription.canceledUtc,
      version: this.subscription.version + 1
    };

    this.providerEvent.processing_status = "processed";

    return {
      subscriptionChanged: true,
      eventProcessed: true
    };
  }

  async markProviderEventIgnored(value) {
    this.ignoredCalls.push(value);
    this.providerEvent.processing_status = "ignored";
    return { changed: true };
  }

  async markProviderEventRetryableFailure(value) {
    this.failureCalls.push(value);
    return { changed: true };
  }
}

class LicensingStore {
  constructor() {
    this.entitlement = entitlement();
    this.commitCalls = [];
  }

  async findEntitlement() {
    return this.entitlement;
  }

  async commitTransition(value) {
    this.commitCalls.push(value);

    return {
      duplicate: false,
      entitlementChanged: true,
      auditWritten: true
    };
  }
}

function orchestrator({
  billingStore = new BillingStore(),
  licensingStore = new LicensingStore()
} = {}) {
  let id = 0;

  return {
    billingStore,
    licensingStore,
    sut: new CommercialEventOrchestrator({
      billingStore,
      licensingStore,
      idFactory(prefix) {
        id += 1;
        return `${prefix}-${id}`;
      }
    })
  };
}

test("successful renewal updates billing and licensing projection", async () => {
  const { sut, billingStore, licensingStore } =
    orchestrator();

  const result = await sut.process({
    providerEvent: providerEvent(),
    providerSubscriptionRef: "external-sub-1",
    subscriptionId: "sub-1",
    commercialAction: "renewal_succeeded",
    seatLimit: 1,
    periodStartsUtc: "2026-08-21T00:00:00Z",
    periodEndsUtc: "2027-08-21T00:00:00Z",
    paymentGraceEndsUtc: null
  }, "2026-08-21T10:00:01Z");

  assert.deepEqual(result, {
    duplicate: false,
    billingChanged: true,
    licensingChanged: true,
    ignoredOutOfOrder: false
  });

  assert.equal(
    billingStore.transitionCalls.length,
    1
  );

  assert.equal(
    licensingStore.commitCalls.length,
    1
  );

  const licensing =
    licensingStore.commitCalls[0];

  assert.equal(
    licensing.entitlement.state,
    "active"
  );

  assert.equal(
    licensing.entitlement.periodEndsUtc,
    "2027-08-21T00:00:00Z"
  );

  assert.equal(
    licensing.entitlement.lastCommercialEventUtc,
    "2026-08-21T10:00:00Z"
  );
});

test("payment failure projects into bounded grace entitlement", async () => {
  const { sut, licensingStore } =
    orchestrator();

  await sut.process({
    providerEvent: providerEvent(),
    subscriptionId: "sub-1",
    commercialAction: "payment_failed",
    seatLimit: 1,
    periodEndsUtc: "2026-08-20T00:00:00Z",
    paymentGraceEndsUtc: "2026-08-28T00:00:00Z"
  }, "2026-08-21T10:00:01Z");

  const call =
    licensingStore.commitCalls[0];

  assert.equal(
    call.entitlement.state,
    "grace"
  );

  assert.equal(
    call.entitlement.paymentGraceEndsUtc,
    "2026-08-28T00:00:00Z"
  );
});

test("older provider event is ignored without mutating billing or licensing", async () => {
  const billingStore = new BillingStore();

  billingStore.subscription =
    subscription({
      last_provider_event_utc:
        "2026-08-21T12:00:00Z"
    });

  const licensingStore =
    new LicensingStore();

  const { sut } = orchestrator({
    billingStore,
    licensingStore
  });

  const result = await sut.process({
    providerEvent: providerEvent({
      occurredUtc: "2026-08-21T11:00:00Z"
    }),
    subscriptionId: "sub-1",
    commercialAction: "payment_failed",
    seatLimit: 1,
    periodEndsUtc: "2026-08-20T00:00:00Z",
    paymentGraceEndsUtc: "2026-08-28T00:00:00Z"
  }, "2026-08-21T12:01:00Z");

  assert.deepEqual(result, {
    duplicate: false,
    billingChanged: false,
    licensingChanged: false,
    ignoredOutOfOrder: true
  });

  assert.equal(
    billingStore.transitionCalls.length,
    0
  );

  assert.equal(
    billingStore.ignoredCalls.length,
    1
  );

  assert.equal(
    licensingStore.commitCalls.length,
    0
  );
});

test("already processed duplicate is a no-op", async () => {
  const billingStore = new BillingStore();

  billingStore.providerEvent = {
    processing_status: "processed"
  };

  const licensingStore =
    new LicensingStore();

  const { sut } = orchestrator({
    billingStore,
    licensingStore
  });

  const result = await sut.process({
    providerEvent: providerEvent(),
    subscriptionId: "sub-1",
    commercialAction: "renewal_succeeded",
    seatLimit: 1,
    periodEndsUtc: "2027-08-21T00:00:00Z"
  }, "2026-08-21T10:00:01Z");

  assert.deepEqual(result, {
    duplicate: true,
    billingChanged: false,
    licensingChanged: false,
    ignoredOutOfOrder: false
  });

  assert.equal(
    billingStore.transitionCalls.length,
    0
  );

  assert.equal(
    licensingStore.commitCalls.length,
    0
  );
});

test("missing subscription becomes retryable provider failure", async () => {
  const billingStore = new BillingStore();
  billingStore.subscription = null;

  const { sut } = orchestrator({
    billingStore
  });

  await assert.rejects(
    () => sut.process({
      providerEvent: providerEvent(),
      subscriptionId: "missing",
      commercialAction: "renewal_succeeded",
      seatLimit: 1,
      periodEndsUtc: "2027-08-21T00:00:00Z"
    }, "2026-08-21T10:00:01Z"),
    (error) =>
      error instanceof CommercialEventError &&
      error.code === "subscription_not_found"
  );

  assert.equal(
    billingStore.failureCalls.length,
    1
  );
});

test("canceled subscription remains licensed until paid-through date", async () => {
  const { sut, licensingStore } =
    orchestrator();

  await sut.process({
    providerEvent: providerEvent(),
    subscriptionId: "sub-1",
    commercialAction: "canceled",
    seatLimit: 1,
    periodEndsUtc: "2026-09-01T00:00:00Z",
    cancelAtPeriodEnd: true,
    canceledUtc: "2026-08-21T10:00:00Z"
  }, "2026-08-21T10:00:01Z");

  assert.equal(
    licensingStore.commitCalls[0].entitlement.state,
    "active"
  );
});

test("orchestrator refuses incomplete dependencies", () => {
  assert.throws(
    () => new CommercialEventOrchestrator({
      billingStore: null,
      licensingStore: {},
      idFactory() {}
    }),
    /billingStore is required/
  );

  assert.throws(
    () => new CommercialEventOrchestrator({
      billingStore: {},
      licensingStore: null,
      idFactory() {}
    }),
    /licensingStore is required/
  );
});