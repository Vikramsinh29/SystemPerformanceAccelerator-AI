import assert from "node:assert/strict";
import {
  createHmac
} from "node:crypto";
import test from "node:test";

import {
  PaddleWebhookError,
  parsePaddleWebhookJson,
  verifyPaddleWebhook
} from "../src/paddle-webhook-verifier.js";

const secret =
  "pdl_webhook_secret_1234567890";

const timestamp =
  1787320800;

const now =
  timestamp * 1000;

function sign(rawBody, ts = timestamp) {
  return createHmac("sha256", secret)
    .update(`${ts}:${rawBody}`, "utf8")
    .digest("hex");
}

test("valid Paddle signature verifies raw body", () => {
  const rawBody =
    '{"event_id":"evt_1","event_type":"subscription.updated"}';

  const result =
    verifyPaddleWebhook({
      rawBody,
      signatureHeader:
        `ts=${timestamp};h1=${sign(rawBody)}`,
      secret,
      now
    });

  assert.deepEqual(result, {
    verified: true,
    timestamp
  });
});

test("multiple h1 signatures are supported", () => {
  const rawBody =
    '{"event_id":"evt_2"}';

  const good =
    sign(rawBody);

  const result =
    verifyPaddleWebhook({
      rawBody,
      signatureHeader:
        `ts=${timestamp};h1=${"0".repeat(64)};h1=${good}`,
      secret,
      now
    });

  assert.equal(
    result.verified,
    true
  );
});

test("modified body fails signature verification", () => {
  const original =
    '{"event_id":"evt_3"}';

  const modified =
    '{"event_id":"evt_4"}';

  assert.throws(
    () => verifyPaddleWebhook({
      rawBody: modified,
      signatureHeader:
        `ts=${timestamp};h1=${sign(original)}`,
      secret,
      now
    }),
    (error) =>
      error instanceof PaddleWebhookError &&
      error.code === "signature_mismatch"
  );
});

test("stale webhook timestamp is rejected", () => {
  const rawBody =
    '{"event_id":"evt_5"}';

  const oldTimestamp =
    timestamp - 1000;

  assert.throws(
    () => verifyPaddleWebhook({
      rawBody,
      signatureHeader:
        `ts=${oldTimestamp};h1=${sign(rawBody, oldTimestamp)}`,
      secret,
      now,
      toleranceSeconds: 300
    }),
    (error) =>
      error instanceof PaddleWebhookError &&
      error.code === "signature_timestamp_out_of_range"
  );
});

test("missing signature header is rejected", () => {
  assert.throws(
    () => verifyPaddleWebhook({
      rawBody: "{}",
      signatureHeader: null,
      secret,
      now
    }),
    (error) =>
      error instanceof PaddleWebhookError &&
      error.code === "missing_signature"
  );
});

test("invalid webhook secret fails before verification", () => {
  assert.throws(
    () => verifyPaddleWebhook({
      rawBody: "{}",
      signatureHeader:
        `ts=${timestamp};h1=${"0".repeat(64)}`,
      secret: "short",
      now
    }),
    (error) =>
      error instanceof PaddleWebhookError &&
      error.code === "invalid_webhook_secret"
  );
});

test("malformed signature timestamp is rejected", () => {
  assert.throws(
    () => verifyPaddleWebhook({
      rawBody: "{}",
      signatureHeader:
        `ts=abc;h1=${"0".repeat(64)}`,
      secret,
      now
    }),
    (error) =>
      error instanceof PaddleWebhookError &&
      error.code === "invalid_signature_timestamp"
  );
});

test("missing h1 hash is rejected", () => {
  assert.throws(
    () => verifyPaddleWebhook({
      rawBody: "{}",
      signatureHeader:
        `ts=${timestamp}`,
      secret,
      now
    }),
    (error) =>
      error instanceof PaddleWebhookError &&
      error.code === "missing_signature_hash"
  );
});

test("webhook JSON is parsed only after raw-body verification boundary", () => {
  const rawBody =
    '{"event_id":"evt_6","data":{"id":"sub_1"}}';

  const parsed =
    parsePaddleWebhookJson(rawBody);

  assert.equal(
    parsed.event_id,
    "evt_6"
  );

  assert.equal(
    parsed.data.id,
    "sub_1"
  );
});

test("malformed webhook JSON is sanitized", () => {
  assert.throws(
    () => parsePaddleWebhookJson("{broken"),
    (error) =>
      error instanceof PaddleWebhookError &&
      error.code === "invalid_json"
  );
});

test("errors do not expose webhook secret", () => {
  try {
    verifyPaddleWebhook({
      rawBody: "{}",
      signatureHeader:
        `ts=${timestamp};h1=${"0".repeat(64)}`,
      secret,
      now
    });

    assert.fail("Expected signature failure.");
  } catch (error) {
    const serialized =
      JSON.stringify({
        code: error.code,
        message: error.message
      });

    assert.doesNotMatch(
      serialized,
      /pdl_webhook_secret/
    );
  }
});