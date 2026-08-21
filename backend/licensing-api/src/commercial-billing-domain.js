export const BILLING_STATES = Object.freeze([
  "pending",
  "active",
  "past_due",
  "grace",
  "canceled",
  "expired",
  "suspended"
]);

const STATE_SET = new Set(BILLING_STATES);

const ALLOWED_TRANSITIONS = Object.freeze({
  pending: new Set([
    "active",
    "canceled",
    "expired",
    "suspended"
  ]),

  active: new Set([
    "past_due",
    "grace",
    "canceled",
    "expired",
    "suspended"
  ]),

  past_due: new Set([
    "active",
    "grace",
    "canceled",
    "expired",
    "suspended"
  ]),

  grace: new Set([
    "active",
    "canceled",
    "expired",
    "suspended"
  ]),

  canceled: new Set([
    "expired"
  ]),

  expired: new Set([]),

  suspended: new Set([
    "active",
    "canceled",
    "expired"
  ])
});

export function transitionBillingState(currentState, nextState) {
  requireBillingState(currentState, "currentState");
  requireBillingState(nextState, "nextState");

  if (currentState === nextState) {
    return {
      changed: false,
      state: currentState
    };
  }

  if (!ALLOWED_TRANSITIONS[currentState].has(nextState)) {
    throw new BillingDomainError(
      "invalid_state_transition",
      `Billing transition ${currentState} -> ${nextState} is not allowed.`
    );
  }

  return {
    changed: true,
    state: nextState
  };
}

export function deriveEntitlementProjection(subscription, nowUtc) {
  requireSubscriptionProjectionInput(subscription);

  const now = parseIso(nowUtc, "nowUtc");
  const state = subscription.state;

  if (state === "suspended") {
    return {
      state: "suspended",
      usable: false
    };
  }

  if (state === "pending") {
    return {
      state: "pending",
      usable: false
    };
  }

  if (state === "expired") {
    return {
      state: "expired",
      usable: false
    };
  }

  const periodEnd = subscription.periodEndsUtc
    ? parseIso(subscription.periodEndsUtc, "periodEndsUtc")
    : null;

  if (
    (state === "active" || state === "canceled") &&
    periodEnd !== null
  ) {
    if (now <= periodEnd) {
      return {
        state: "active",
        usable: true
      };
    }

    return {
      state: "expired",
      usable: false
    };
  }

  if (state === "active") {
    return {
      state: "active",
      usable: true
    };
  }

  if (state === "past_due" || state === "grace") {
    if (!subscription.paymentGraceEndsUtc) {
      return {
        state: "expired",
        usable: false
      };
    }

    const graceEnd = parseIso(
      subscription.paymentGraceEndsUtc,
      "paymentGraceEndsUtc"
    );

    if (now <= graceEnd) {
      return {
        state: "grace",
        usable: true
      };
    }

    return {
      state: "expired",
      usable: false
    };
  }

  if (state === "canceled") {
    return {
      state: "expired",
      usable: false
    };
  }

  throw new BillingDomainError(
    "unsupported_projection_state",
    `Unsupported billing projection state: ${state}`
  );
}

export function validateMoneySnapshot(snapshot) {
  if (!snapshot || typeof snapshot !== "object") {
    throw new BillingDomainError(
      "invalid_money_snapshot",
      "Money snapshot is required."
    );
  }

  requireCurrency(snapshot.currency);

  const nonNegativeFields = [
    "listAmountMinor",
    "discountAmountMinor",
    "subtotalMinor",
    "taxAmountMinor",
    "grossAmountMinor",
    "processorFeeMinor",
    "processorFeeTaxMinor",
    "refundAmountMinor",
    "chargebackAmountMinor"
  ];

  for (const field of nonNegativeFields) {
    requireMinorUnits(snapshot[field], field, false);
  }

  requireMinorUnits(
    snapshot.netReceivableMinor,
    "netReceivableMinor",
    true
  );

  return Object.freeze({
    ...snapshot
  });
}

export function normalizeProviderEvent(value) {
  if (!value || typeof value !== "object") {
    throw new BillingDomainError(
      "invalid_provider_event",
      "Provider event is required."
    );
  }

  const provider = requireText(value.provider, "provider");
  const providerEventId =
    requireText(value.providerEventId, "providerEventId");
  const eventType =
    requireText(value.eventType, "eventType");
  const payloadSha256 =
    requireSha256(value.payloadSha256);

  const occurredUtc =
    requireIsoText(value.occurredUtc, "occurredUtc");

  return Object.freeze({
    provider: provider.toLowerCase(),
    providerEventId,
    eventType,
    occurredUtc,
    payloadSha256: payloadSha256.toLowerCase()
  });
}

export function validatePlan(value) {
  if (!value || typeof value !== "object") {
    throw new BillingDomainError(
      "invalid_plan",
      "Plan is required."
    );
  }

  const planId = requireText(value.planId, "planId");
  const productId = requireText(value.productId, "productId");
  const planCode = requireText(value.planCode, "planCode");

  if (!["monthly", "annual"].includes(value.billingInterval)) {
    throw new BillingDomainError(
      "invalid_billing_interval",
      "billingInterval must be monthly or annual."
    );
  }

  if (!Number.isSafeInteger(value.seatLimit) || value.seatLimit < 1) {
    throw new BillingDomainError(
      "invalid_seat_limit",
      "seatLimit must be a positive integer."
    );
  }

  return Object.freeze({
    planId,
    productId,
    planCode,
    billingInterval: value.billingInterval,
    seatLimit: value.seatLimit
  });
}

function requireSubscriptionProjectionInput(value) {
  if (!value || typeof value !== "object") {
    throw new BillingDomainError(
      "invalid_subscription",
      "Subscription is required."
    );
  }

  requireBillingState(value.state, "subscription.state");

  if (value.periodEndsUtc !== null &&
      value.periodEndsUtc !== undefined) {
    requireIsoText(value.periodEndsUtc, "periodEndsUtc");
  }

  if (value.paymentGraceEndsUtc !== null &&
      value.paymentGraceEndsUtc !== undefined) {
    requireIsoText(
      value.paymentGraceEndsUtc,
      "paymentGraceEndsUtc"
    );
  }
}

function requireBillingState(value, field) {
  if (!STATE_SET.has(value)) {
    throw new BillingDomainError(
      "invalid_billing_state",
      `${field} is invalid.`
    );
  }
}

function requireCurrency(value) {
  if (
    typeof value !== "string" ||
    !/^[A-Z]{3}$/.test(value)
  ) {
    throw new BillingDomainError(
      "invalid_currency",
      "currency must be a 3-letter uppercase ISO-style code."
    );
  }
}

function requireMinorUnits(value, field, allowNegative) {
  if (!Number.isSafeInteger(value)) {
    throw new BillingDomainError(
      "invalid_money_amount",
      `${field} must be a safe integer minor-unit amount.`
    );
  }

  if (!allowNegative && value < 0) {
    throw new BillingDomainError(
      "invalid_money_amount",
      `${field} cannot be negative.`
    );
  }
}

function requireText(value, field) {
  if (
    typeof value !== "string" ||
    value.trim().length === 0 ||
    value.length > 256
  ) {
    throw new BillingDomainError(
      "invalid_text",
      `${field} is invalid.`
    );
  }

  return value.trim();
}

function requireSha256(value) {
  if (
    typeof value !== "string" ||
    !/^[a-fA-F0-9]{64}$/.test(value)
  ) {
    throw new BillingDomainError(
      "invalid_payload_hash",
      "payloadSha256 must be a 64-character hexadecimal SHA-256."
    );
  }

  return value;
}

function requireIsoText(value, field) {
  parseIso(value, field);
  return value;
}

function parseIso(value, field) {
  if (
    typeof value !== "string" ||
    value.trim().length === 0
  ) {
    throw new BillingDomainError(
      "invalid_timestamp",
      `${field} is invalid.`
    );
  }

  const parsed = Date.parse(value);

  if (Number.isNaN(parsed)) {
    throw new BillingDomainError(
      "invalid_timestamp",
      `${field} is invalid.`
    );
  }

  return parsed;
}

export class BillingDomainError extends Error {
  constructor(code, message) {
    super(message);
    this.name = "BillingDomainError";
    this.code = code;
  }
}