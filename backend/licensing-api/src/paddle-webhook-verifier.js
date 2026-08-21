import {
  createHmac,
  timingSafeEqual
} from "node:crypto";

const DEFAULT_TOLERANCE_SECONDS = 300;

export function verifyPaddleWebhook({
  rawBody,
  signatureHeader,
  secret,
  now = Date.now(),
  toleranceSeconds = DEFAULT_TOLERANCE_SECONDS
}) {
  if (typeof rawBody !== "string") {
    throw new PaddleWebhookError(
      "invalid_raw_body",
      "Raw webhook body is required."
    );
  }

  validateSecret(secret);

  if (
    !Number.isSafeInteger(toleranceSeconds) ||
    toleranceSeconds < 1 ||
    toleranceSeconds > 3600
  ) {
    throw new PaddleWebhookError(
      "invalid_tolerance",
      "Webhook tolerance is invalid."
    );
  }

  const parsed = parseSignatureHeader(signatureHeader);

  const timestampMs =
    parsed.timestamp * 1000;

  const ageMs =
    Math.abs(now - timestampMs);

  if (ageMs > toleranceSeconds * 1000) {
    throw new PaddleWebhookError(
      "signature_timestamp_out_of_range",
      "Webhook signature timestamp is outside the allowed window."
    );
  }

  const signedPayload =
    `${parsed.timestamp}:${rawBody}`;

  const expected =
    createHmac("sha256", secret)
      .update(signedPayload, "utf8")
      .digest("hex");

  const expectedBuffer =
    Buffer.from(expected, "hex");

  const candidateBuffers =
    parsed.signatures
      .filter((value) =>
        /^[a-fA-F0-9]{64}$/.test(value)
      )
      .map((value) =>
        Buffer.from(value, "hex")
      );

  const matched =
    candidateBuffers.some((candidate) =>
      candidate.length === expectedBuffer.length &&
      timingSafeEqual(candidate, expectedBuffer)
    );

  if (!matched) {
    throw new PaddleWebhookError(
      "signature_mismatch",
      "Webhook signature is invalid."
    );
  }

  return Object.freeze({
    verified: true,
    timestamp: parsed.timestamp
  });
}

export function parsePaddleWebhookJson(rawBody) {
  if (typeof rawBody !== "string") {
    throw new PaddleWebhookError(
      "invalid_raw_body",
      "Raw webhook body is required."
    );
  }

  try {
    return JSON.parse(rawBody);
  } catch {
    throw new PaddleWebhookError(
      "invalid_json",
      "Webhook payload is malformed."
    );
  }
}

function parseSignatureHeader(value) {
  if (
    typeof value !== "string" ||
    value.trim().length === 0
  ) {
    throw new PaddleWebhookError(
      "missing_signature",
      "Paddle-Signature header is required."
    );
  }

  let timestamp = null;
  const signatures = [];

  for (const part of value.split(";")) {
    const [rawKey, ...rawValueParts] =
      part.trim().split("=");

    const key = rawKey?.trim();
    const fieldValue =
      rawValueParts.join("=").trim();

    if (key === "ts") {
      if (!/^\d+$/.test(fieldValue)) {
        throw new PaddleWebhookError(
          "invalid_signature_timestamp",
          "Webhook signature timestamp is invalid."
        );
      }

      timestamp = Number(fieldValue);
    }

    if (key === "h1" && fieldValue) {
      signatures.push(fieldValue);
    }
  }

  if (
    !Number.isSafeInteger(timestamp) ||
    timestamp <= 0
  ) {
    throw new PaddleWebhookError(
      "missing_signature_timestamp",
      "Webhook signature timestamp is missing."
    );
  }

  if (signatures.length === 0) {
    throw new PaddleWebhookError(
      "missing_signature_hash",
      "Webhook signature hash is missing."
    );
  }

  return {
    timestamp,
    signatures
  };
}

function validateSecret(secret) {
  if (
    typeof secret !== "string" ||
    secret.trim().length < 16
  ) {
    throw new PaddleWebhookError(
      "invalid_webhook_secret",
      "A valid webhook secret is required."
    );
  }
}

export class PaddleWebhookError extends Error {
  constructor(code, message) {
    super(message);
    this.name = "PaddleWebhookError";
    this.code = code;
  }
}