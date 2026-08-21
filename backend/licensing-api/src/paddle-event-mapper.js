const SUBSCRIPTION_EVENTS = new Set([
  "subscription.created",
  "subscription.activated",
  "subscription.updated",
  "subscription.canceled",
  "subscription.past_due",
  "subscription.resumed"
]);

export function mapPaddleEvent({
  payload,
  payloadSha256
}) {
  validateEnvelope(payload);
  validateSha256(payloadSha256);

  const providerEvent = Object.freeze({
    provider: "paddle",
    providerEventId: payload.event_id,
    eventType: payload.event_type,
    occurredUtc: payload.occurred_at,
    payloadSha256: payloadSha256.toLowerCase()
  });

  if (!SUBSCRIPTION_EVENTS.has(payload.event_type)) {
    return Object.freeze({
      disposition: "ignored",
      providerEvent,
      reason: "non_entitlement_event"
    });
  }

  const data = payload.data;

  if (!data || typeof data !== "object") {
    throw new PaddleEventMappingError(
      "missing_event_data",
      "Paddle subscription event data is required."
    );
  }

  requireText(data.id, "data.id");

  const status = normalizeStatus(data.status);

  const commercialAction =
    resolveCommercialAction(
      payload.event_type,
      status
    );

  if (!commercialAction) {
    return Object.freeze({
      disposition: "ignored",
      providerEvent,
      providerSubscriptionRef: data.id,
      reason: "unsupported_subscription_state"
    });
  }

  const currentPeriod =
    data.current_billing_period ?? null;

  const periodStartsUtc =
    normalizeOptionalIso(
      currentPeriod?.starts_at,
      "current_billing_period.starts_at"
    );

  const periodEndsUtc =
    normalizeOptionalIso(
      currentPeriod?.ends_at,
      "current_billing_period.ends_at"
    );

  const canceledUtc =
    normalizeOptionalIso(
      data.canceled_at,
      "data.canceled_at"
    );

  return Object.freeze({
    disposition: "commercial",
    providerEvent,
    providerSubscriptionRef: data.id,
    commercialAction,
    periodStartsUtc,
    periodEndsUtc,
    canceledUtc,
    cancelAtPeriodEnd:
      commercialAction === "canceled" &&
      periodEndsUtc !== null,
    providerStatus: status
  });
}

function resolveCommercialAction(
  eventType,
  status
) {
  if (eventType === "subscription.past_due") {
    return "payment_failed";
  }

  if (eventType === "subscription.canceled") {
    return "canceled";
  }

  if (eventType === "subscription.resumed") {
    return "renewal_succeeded";
  }

  if (
    eventType === "subscription.activated"
  ) {
    return "payment_activated";
  }

  if (
    eventType === "subscription.created" ||
    eventType === "subscription.updated"
  ) {
    if (status === "active") {
      return "renewal_succeeded";
    }

    if (status === "past_due") {
      return "payment_failed";
    }

    if (status === "canceled") {
      return "canceled";
    }

    if (status === "paused") {
      return "suspended";
    }
  }

  return null;
}

function normalizeStatus(value) {
  if (value === null || value === undefined) {
    return null;
  }

  requireText(value, "data.status");

  return value.trim().toLowerCase();
}

function validateEnvelope(value) {
  if (!value || typeof value !== "object") {
    throw new PaddleEventMappingError(
      "invalid_event",
      "Paddle event payload is required."
    );
  }

  requireText(value.event_id, "event_id");
  requireText(value.event_type, "event_type");
  requireIso(value.occurred_at, "occurred_at");
}

function validateSha256(value) {
  if (
    typeof value !== "string" ||
    !/^[a-fA-F0-9]{64}$/.test(value)
  ) {
    throw new PaddleEventMappingError(
      "invalid_payload_hash",
      "payloadSha256 is invalid."
    );
  }
}

function normalizeOptionalIso(value, field) {
  if (value === null || value === undefined) {
    return null;
  }

  requireIso(value, field);
  return value;
}

function requireText(value, field) {
  if (
    typeof value !== "string" ||
    value.trim().length === 0
  ) {
    throw new PaddleEventMappingError(
      "invalid_field",
      `${field} is required.`
    );
  }
}

function requireIso(value, field) {
  requireText(value, field);

  if (Number.isNaN(Date.parse(value))) {
    throw new PaddleEventMappingError(
      "invalid_timestamp",
      `${field} must be ISO-8601.`
    );
  }
}

export class PaddleEventMappingError extends Error {
  constructor(code, message) {
    super(message);
    this.name = "PaddleEventMappingError";
    this.code = code;
  }
}