import assert from "node:assert/strict";
import test from "node:test";

import {
  PaddleEventMappingError,
  mapPaddleEvent
} from "../src/paddle-event-mapper.js";

const hash =
  "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

function envelope(eventType, data) {
  return {
    event_id: "evt_123",
    event_type: eventType,
    occurred_at: "2026-08-21T10:00:00Z",
    data
  };
}

function subscriptionData(overrides = {}) {
  return {
    id: "sub_123",
    status: "active",
    current_billing_period: {
      starts_at: "2026-08-21T00:00:00Z",
      ends_at: "2027-08-21T00:00:00Z"
    },
    canceled_at: null,
    ...overrides
  };
}

test("active subscription event maps to provider-neutral renewal", () => {
  const result = mapPaddleEvent({
    payload: envelope(
      "subscription.updated",
      subscriptionData()
    ),
    payloadSha256: hash
  });

  assert.equal(result.disposition, "commercial");
  assert.equal(result.providerEvent.provider, "paddle");
  assert.equal(
    result.providerSubscriptionRef,
    "sub_123"
  );
  assert.equal(
    result.commercialAction,
    "renewal_succeeded"
  );
  assert.equal(
    result.periodEndsUtc,
    "2027-08-21T00:00:00Z"
  );
});

test("past-due subscription maps to payment failure", () => {
  const result = mapPaddleEvent({
    payload: envelope(
      "subscription.past_due",
      subscriptionData({
        status: "past_due"
      })
    ),
    payloadSha256: hash
  });

  assert.equal(
    result.commercialAction,
    "payment_failed"
  );
});

test("canceled subscription preserves paid-through period", () => {
  const result = mapPaddleEvent({
    payload: envelope(
      "subscription.canceled",
      subscriptionData({
        status: "canceled",
        canceled_at: "2026-08-21T10:00:00Z"
      })
    ),
    payloadSha256: hash
  });

  assert.equal(
    result.commercialAction,
    "canceled"
  );

  assert.equal(
    result.cancelAtPeriodEnd,
    true
  );

  assert.equal(
    result.periodEndsUtc,
    "2027-08-21T00:00:00Z"
  );
});

test("activated subscription maps to first payment activation", () => {
  const result = mapPaddleEvent({
    payload: envelope(
      "subscription.activated",
      subscriptionData()
    ),
    payloadSha256: hash
  });

  assert.equal(
    result.commercialAction,
    "payment_activated"
  );
});

test("paused subscription maps to suspension", () => {
  const result = mapPaddleEvent({
    payload: envelope(
      "subscription.updated",
      subscriptionData({
        status: "paused"
      })
    ),
    payloadSha256: hash
  });

  assert.equal(
    result.commercialAction,
    "suspended"
  );
});

test("transaction events do not mutate entitlement state", () => {
  const result = mapPaddleEvent({
    payload: envelope(
      "transaction.completed",
      {
        id: "txn_123"
      }
    ),
    payloadSha256: hash
  });

  assert.deepEqual(result, {
    disposition: "ignored",
    providerEvent: {
      provider: "paddle",
      providerEventId: "evt_123",
      eventType: "transaction.completed",
      occurredUtc: "2026-08-21T10:00:00Z",
      payloadSha256: hash
    },
    reason: "non_entitlement_event"
  });
});

test("unknown provider event is a safe no-op", () => {
  const result = mapPaddleEvent({
    payload: envelope(
      "something.future",
      {}
    ),
    payloadSha256: hash
  });

  assert.equal(
    result.disposition,
    "ignored"
  );
});

test("malformed subscription payload fails closed", () => {
  assert.throws(
    () => mapPaddleEvent({
      payload: envelope(
        "subscription.updated",
        {}
      ),
      payloadSha256: hash
    }),
    (error) =>
      error instanceof PaddleEventMappingError &&
      error.code === "invalid_field"
  );
});

test("invalid event timestamp fails closed", () => {
  const payload = envelope(
    "subscription.updated",
    subscriptionData()
  );

  payload.occurred_at = "not-a-date";

  assert.throws(
    () => mapPaddleEvent({
      payload,
      payloadSha256: hash
    }),
    (error) =>
      error instanceof PaddleEventMappingError &&
      error.code === "invalid_timestamp"
  );
});

test("provider-specific fields do not leak into commercial identifiers", () => {
  const result = mapPaddleEvent({
    payload: envelope(
      "subscription.updated",
      subscriptionData()
    ),
    payloadSha256: hash
  });

  assert.equal(
    result.providerEvent.provider,
    "paddle"
  );

  assert.equal(
    Object.hasOwn(result, "paddleSubscriptionId"),
    false
  );

  assert.equal(
    Object.hasOwn(result, "paddleCustomerId"),
    false
  );
});