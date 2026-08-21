import {
  parsePaddleWebhookJson,
  verifyPaddleWebhook
} from "./paddle-webhook-verifier.js";

import {
  mapPaddleEvent
} from "./paddle-event-mapper.js";

export function createPaddleWebhookIngress({
  webhookSecret,
  billingStore,
  orchestrator,
  hashPayload,
  nowProvider = () => Date.now(),
  gracePeriodSeconds = 7 * 24 * 60 * 60
}) {
  if (
    typeof webhookSecret !== "string" ||
    webhookSecret.length < 16
  ) {
    throw new TypeError(
      "A valid Paddle webhook secret is required."
    );
  }

  if (!billingStore) {
    throw new TypeError(
      "billingStore is required."
    );
  }

  if (!orchestrator) {
    throw new TypeError(
      "orchestrator is required."
    );
  }

  if (typeof hashPayload !== "function") {
    throw new TypeError(
      "hashPayload is required."
    );
  }

  if (typeof nowProvider !== "function") {
    throw new TypeError(
      "nowProvider is required."
    );
  }

  if (
    !Number.isSafeInteger(gracePeriodSeconds) ||
    gracePeriodSeconds < 0
  ) {
    throw new TypeError(
      "gracePeriodSeconds is invalid."
    );
  }

  return async function handlePaddleWebhook(request) {
    if (!request || request.method !== "POST") {
      return jsonResponse(
        405,
        {
          error: "method_not_allowed"
        },
        {
          Allow: "POST"
        }
      );
    }

    const rawBody =
      await readRawBody(request);

    const signatureHeader =
      readHeader(
        request.headers,
        "Paddle-Signature"
      );

    verifyPaddleWebhook({
      rawBody,
      signatureHeader,
      secret: webhookSecret,
      now: nowProvider()
    });

    const payload =
      parsePaddleWebhookJson(rawBody);

    const payloadSha256 =
      await hashPayload(rawBody);

    const mapped =
      mapPaddleEvent({
        payload,
        payloadSha256
      });

    if (mapped.disposition === "ignored") {
      return jsonResponse(
        200,
        {
          accepted: true,
          ignored: true,
          reason: mapped.reason
        }
      );
    }

    const mapping =
      await billingStore.findProviderMapping(
        "paddle",
        "subscription",
        mapped.providerSubscriptionRef
      );

    if (!mapping) {
      return jsonResponse(
        202,
        {
          accepted: true,
          deferred: true,
          reason: "subscription_mapping_not_found"
        }
      );
    }

    const nowUtc =
      new Date(nowProvider()).toISOString();

    const paymentGraceEndsUtc =
      mapped.commercialAction === "payment_failed"
        ? deriveGraceEnd(
            mapped.periodEndsUtc,
            gracePeriodSeconds
          )
        : null;

    const result =
      await orchestrator.process({
        providerEvent:
          mapped.providerEvent,

        providerSubscriptionRef:
          mapped.providerSubscriptionRef,

        subscriptionId:
          mapping.internal_entity_id,

        commercialAction:
          mapped.commercialAction,

        seatLimit:
          mapping.seat_limit ?? 1,

        periodStartsUtc:
          mapped.periodStartsUtc,

        periodEndsUtc:
          mapped.periodEndsUtc,

        paymentGraceEndsUtc,

        cancelAtPeriodEnd:
          mapped.cancelAtPeriodEnd ?? false,

        canceledUtc:
          mapped.canceledUtc
      }, nowUtc);

    return jsonResponse(
      200,
      {
        accepted: true,
        ignored: false,
        result
      }
    );
  };
}

async function readRawBody(request) {
  if (typeof request.text !== "function") {
    throw new PaddleWebhookIngressError(
      "invalid_request",
      "Webhook request body is unavailable."
    );
  }

  const rawBody =
    await request.text();

  if (typeof rawBody !== "string") {
    throw new PaddleWebhookIngressError(
      "invalid_request",
      "Webhook request body is invalid."
    );
  }

  return rawBody;
}

function readHeader(headers, name) {
  if (!headers) {
    return null;
  }

  if (typeof headers.get === "function") {
    return headers.get(name);
  }

  const target =
    name.toLowerCase();

  for (const [key, value] of Object.entries(headers)) {
    if (key.toLowerCase() === target) {
      return value;
    }
  }

  return null;
}

function deriveGraceEnd(
  periodEndsUtc,
  gracePeriodSeconds
) {
  const base =
    periodEndsUtc
      ? Date.parse(periodEndsUtc)
      : NaN;

  if (Number.isNaN(base)) {
    throw new PaddleWebhookIngressError(
      "missing_period_end",
      "A valid period end is required for payment grace."
    );
  }

  return new Date(
    base + gracePeriodSeconds * 1000
  ).toISOString();
}

function jsonResponse(
  status,
  body,
  extraHeaders = {}
) {
  return new Response(
    JSON.stringify(body),
    {
      status,
      headers: {
        "Content-Type": "application/json; charset=utf-8",
        ...extraHeaders
      }
    }
  );
}

export class PaddleWebhookIngressError extends Error {
  constructor(code, message) {
    super(message);
    this.name = "PaddleWebhookIngressError";
    this.code = code;
  }
}