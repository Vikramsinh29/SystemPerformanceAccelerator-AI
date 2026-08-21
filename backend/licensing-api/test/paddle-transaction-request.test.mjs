import assert from "node:assert/strict";
import test from "node:test";

import {
  PaddleTransactionRequestError,
  buildPaddleTransactionHttpRequest,
  buildPaddleTransactionRequest
} from "../src/paddle-transaction-request.js";

const priceId =
  "pri_01h00000000000000000000000";

const sandboxKey =
  "pdl_sdbx_apikey_test_key_material_123456789";

const liveKey =
  "pdl_live_apikey_test_key_material_123456789";

function build(overrides = {}) {
  return buildPaddleTransactionRequest({
    priceId,
    quantity: 1,
    internalAccountId:
      "acct-internal-1",
    internalSubscriptionId:
      "sub-internal-1",
    productCode:
      "pc-spa",
    ...overrides
  });
}

test("builder creates automatic Paddle transaction", () => {
  const body = build();

  assert.deepEqual(
    body.items,
    [
      {
        price_id: priceId,
        quantity: 1
      }
    ]
  );

  assert.equal(
    body.collection_mode,
    "automatic"
  );
});

test("builder carries only provider-neutral internal correlation metadata", () => {
  const body = build();

  assert.deepEqual(
    body.custom_data,
    {
      pcspa_account_id:
        "acct-internal-1",

      pcspa_subscription_id:
        "sub-internal-1",

      pcspa_product:
        "pc-spa"
    }
  );

  assert.equal(
    Object.hasOwn(
      body.custom_data,
      "paddle_subscription_id"
    ),
    false
  );
});

test("seat quantity is preserved", () => {
  const body =
    build({
      quantity: 5
    });

  assert.equal(
    body.items[0].quantity,
    5
  );
});

test("optional checkout URL is normalized", () => {
  const body =
    build({
      checkoutUrl:
        "https://getpcspa.com/checkout"
    });

  assert.equal(
    body.checkout.url,
    "https://getpcspa.com/checkout"
  );
});

test("non-HTTPS checkout URL fails closed", () => {
  assert.throws(
    () => build({
      checkoutUrl:
        "http://getpcspa.com/checkout"
    }),

    (error) =>
      error instanceof
        PaddleTransactionRequestError &&
      error.code ===
        "invalid_checkout_url"
  );
});

test("invalid price ID is rejected", () => {
  assert.throws(
    () => build({
      priceId: "bad"
    }),

    (error) =>
      error instanceof
        PaddleTransactionRequestError &&
      error.code ===
        "invalid_price_id"
  );
});

test("zero quantity is rejected", () => {
  assert.throws(
    () => build({
      quantity: 0
    }),

    (error) =>
      error instanceof
        PaddleTransactionRequestError &&
      error.code ===
        "invalid_quantity"
  );
});

test("sandbox HTTP request targets sandbox transactions endpoint", () => {
  const body = build();

  const request =
    buildPaddleTransactionHttpRequest({
      apiKey: sandboxKey,
      body,
      environment:
        "sandbox"
    });

  assert.equal(
    request.method,
    "POST"
  );

  assert.equal(
    request.url,
    "https://sandbox-api.paddle.com/transactions"
  );

  assert.equal(
    request.headers[
      "Paddle-Version"
    ],
    "1"
  );

  assert.equal(
    request.headers.Authorization,
    `Bearer ${sandboxKey}`
  );
});

test("live HTTP request targets live endpoint only with live key", () => {
  const request =
    buildPaddleTransactionHttpRequest({
      apiKey: liveKey,
      body: build(),
      environment: "live"
    });

  assert.equal(
    request.url,
    "https://api.paddle.com/transactions"
  );
});

test("sandbox refuses live API key", () => {
  assert.throws(
    () =>
      buildPaddleTransactionHttpRequest({
        apiKey: liveKey,
        body: build(),
        environment:
          "sandbox"
      }),

    (error) =>
      error instanceof
        PaddleTransactionRequestError &&
      error.code ===
        "environment_key_mismatch"
  );
});

test("live refuses sandbox API key", () => {
  assert.throws(
    () =>
      buildPaddleTransactionHttpRequest({
        apiKey: sandboxKey,
        body: build(),
        environment:
          "live"
      }),

    (error) =>
      error instanceof
        PaddleTransactionRequestError &&
      error.code ===
        "environment_key_mismatch"
  );
});

test("API key never enters transaction JSON body", () => {
  const request =
    buildPaddleTransactionHttpRequest({
      apiKey: sandboxKey,
      body: build(),
      environment:
        "sandbox"
    });

  assert.doesNotMatch(
    request.body,
    /pdl_sdbx_apikey/
  );
});