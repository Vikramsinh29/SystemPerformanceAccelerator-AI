import assert from "node:assert/strict";
import {
  createHash,
  createHmac
} from "node:crypto";
import test from "node:test";

import {
  createPaddleWebhookIngress
} from "../src/paddle-webhook-ingress.js";

const secret =
  "pdl_webhook_secret_1234567890";

const timestamp =
  1787320800;

const now =
  timestamp * 1000;

function sign(rawBody) {
  return createHmac("sha256", secret)
    .update(
      `${timestamp}:${rawBody}`,
      "utf8"
    )
    .digest("hex");
}

async function hashPayload(rawBody) {
  return createHash("sha256")
    .update(rawBody, "utf8")
    .digest("hex");
}

function request(payload, {
  method = "POST",
  signature = true
} = {}) {
  const rawBody =
    JSON.stringify(payload);

  return {
    method,
    headers: new Headers({
      ...(signature
        ? {
            "Paddle-Signature":
              `ts=${timestamp};h1=${sign(rawBody)}`
          }
        : {})
    }),

    async text() {
      return rawBody;
    }
  };
}

function subscriptionPayload({
  eventType = "subscription.updated",
  status = "active"
} = {}) {
  return {
    event_id: "evt_123",
    event_type: eventType,
    occurred_at:
      "2026-08-21T10:00:00Z",

    data: {
      id: "sub_paddle_123",
      status,

      current_billing_period: {
        starts_at:
          "2026-08-21T00:00:00Z",

        ends_at:
          "2027-08-21T00:00:00Z"
      },

      canceled_at: null
    }
  };
}

class BillingStore {
  constructor(mapping = {
    internal_entity_id: "sub-internal-1",
    seat_limit: 1
  }) {
    this.mapping = mapping;
    this.calls = [];
  }

  async findProviderMapping(
    provider,
    entityType,
    providerRef
  ) {
    this.calls.push({
      provider,
      entityType,
      providerRef
    });

    return this.mapping;
  }
}

class Orchestrator {
  constructor() {
    this.calls = [];
  }

  async process(input, nowUtc) {
    this.calls.push({
      input,
      nowUtc
    });

    return {
      duplicate: false,
      billingChanged: true,
      licensingChanged: true,
      ignoredOutOfOrder: false
    };
  }
}

function create({
  mapping,
  gracePeriodSeconds =
    7 * 24 * 60 * 60
} = {}) {
  const billingStore =
    new BillingStore(mapping);

  const orchestrator =
    new Orchestrator();

  const handler =
    createPaddleWebhookIngress({
      webhookSecret: secret,
      billingStore,
      orchestrator,
      hashPayload,
      nowProvider: () => now,
      gracePeriodSeconds
    });

  return {
    handler,
    billingStore,
    orchestrator
  };
}

test("valid subscription event flows into generic orchestrator", async () => {
  const {
    handler,
    billingStore,
    orchestrator
  } = create();

  const response =
    await handler(
      request(
        subscriptionPayload()
      )
    );

  assert.equal(
    response.status,
    200
  );

  assert.equal(
    billingStore.calls.length,
    1
  );

  assert.deepEqual(
    billingStore.calls[0],
    {
      provider: "paddle",
      entityType: "subscription",
      providerRef: "sub_paddle_123"
    }
  );

  assert.equal(
    orchestrator.calls.length,
    1
  );

  const input =
    orchestrator.calls[0].input;

  assert.equal(
    input.subscriptionId,
    "sub-internal-1"
  );

  assert.equal(
    input.commercialAction,
    "renewal_succeeded"
  );

  assert.equal(
    input.providerEvent.provider,
    "paddle"
  );
});

test("unknown Paddle event is acknowledged without entitlement mutation", async () => {
  const {
    handler,
    billingStore,
    orchestrator
  } = create();

  const response =
    await handler(
      request({
        event_id: "evt_999",
        event_type: "something.future",
        occurred_at:
          "2026-08-21T10:00:00Z",
        data: {}
      })
    );

  const body =
    await response.json();

  assert.equal(
    response.status,
    200
  );

  assert.equal(
    body.ignored,
    true
  );

  assert.equal(
    billingStore.calls.length,
    0
  );

  assert.equal(
    orchestrator.calls.length,
    0
  );
});

test("missing subscription mapping defers processing safely", async () => {
  const {
    handler,
    orchestrator
  } = create({
    mapping: null
  });

  const response =
    await handler(
      request(
        subscriptionPayload()
      )
    );

  const body =
    await response.json();

  assert.equal(
    response.status,
    202
  );

  assert.equal(
    body.deferred,
    true
  );

  assert.equal(
    body.reason,
    "subscription_mapping_not_found"
  );

  assert.equal(
    orchestrator.calls.length,
    0
  );
});

test("payment failure derives bounded grace period", async () => {
  const {
    handler,
    orchestrator
  } = create({
    gracePeriodSeconds:
      7 * 24 * 60 * 60
  });

  await handler(
    request(
      subscriptionPayload({
        eventType:
          "subscription.past_due",
        status:
          "past_due"
      })
    )
  );

  const input =
    orchestrator.calls[0].input;

  assert.equal(
    input.commercialAction,
    "payment_failed"
  );

  assert.equal(
    input.paymentGraceEndsUtc,
    "2027-08-28T00:00:00.000Z"
  );
});

test("invalid signature fails before mapper or orchestrator", async () => {
  const {
    handler,
    billingStore,
    orchestrator
  } = create();

  await assert.rejects(
    () => handler(
      request(
        subscriptionPayload(),
        {
          signature: false
        }
      )
    ),
    /Paddle-Signature header is required/
  );

  assert.equal(
    billingStore.calls.length,
    0
  );

  assert.equal(
    orchestrator.calls.length,
    0
  );
});

test("non-POST requests are rejected without reading payment state", async () => {
  const {
    handler,
    billingStore,
    orchestrator
  } = create();

  const response =
    await handler(
      request(
        subscriptionPayload(),
        {
          method: "GET"
        }
      )
    );

  assert.equal(
    response.status,
    405
  );

  assert.equal(
    response.headers.get("Allow"),
    "POST"
  );

  assert.equal(
    billingStore.calls.length,
    0
  );

  assert.equal(
    orchestrator.calls.length,
    0
  );
});

test("webhook ingress preserves provider-neutral internal subscription ids", async () => {
  const {
    handler,
    orchestrator
  } = create();

  await handler(
    request(
      subscriptionPayload()
    )
  );

  const input =
    orchestrator.calls[0].input;

  assert.equal(
    input.subscriptionId,
    "sub-internal-1"
  );

  assert.notEqual(
    input.subscriptionId,
    "sub_paddle_123"
  );
});