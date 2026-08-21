import assert from "node:assert/strict";
import test from "node:test";

import {
  PaddleApiError,
  createPaddleApiClient
} from "../src/paddle-api-client.js";

import {
  buildPaddleTransactionRequest
} from "../src/paddle-transaction-request.js";

const sandboxKey =
  "pdl_sdbx_apikey_test_key_material_123456789";

const txnId =
  "txn_01h00000000000000000000000";

function transactionBody() {
  return buildPaddleTransactionRequest({
    priceId:
      "pri_01h00000000000000000000000",

    internalAccountId:
      "acct-internal-1",

    internalSubscriptionId:
      "sub-internal-1"
  });
}

function clientWith(response) {
  return createPaddleApiClient({
    apiKey: sandboxKey,

    environment:
      "sandbox",

    transport: {
      async send() {
        return response;
      }
    }
  });
}

test("201 transaction response is normalized", async () => {
  const client =
    clientWith({
      status: 201,

      json: {
        data: {
          id: txnId,
          status: "draft",
          collection_mode:
            "automatic",

          checkout: {
            url:
              "https://checkout.paddle.com/example"
          }
        },

        meta: {
          request_id:
            "request-123"
        }
      }
    });

  const result =
    await client.createTransaction(
      transactionBody()
    );

  assert.deepEqual(
    result,
    {
      transactionId:
        txnId,

      status:
        "draft",

      collectionMode:
        "automatic",

      checkoutUrl:
        "https://checkout.paddle.com/example",

      requestId:
        "request-123"
    }
  );
});

test("201 without valid transaction data fails closed", async () => {
  const client =
    clientWith({
      status: 201,
      json: {
        data: {}
      }
    });

  await assert.rejects(
    client.createTransaction(
      transactionBody()
    ),

    (error) =>
      error instanceof
        PaddleApiError &&
      error.code ===
        "invalid_transaction_id"
  );
});

test("unexpected collection mode fails closed", async () => {
  const client =
    clientWith({
      status: 201,

      json: {
        data: {
          id: txnId,
          status: "draft",
          collection_mode:
            "manual"
        }
      }
    });

  await assert.rejects(
    client.createTransaction(
      transactionBody()
    ),

    (error) =>
      error.code ===
        "unexpected_collection_mode"
  );
});

test("400 maps to sanitized validation error", async () => {
  const client =
    clientWith({
      status: 400,

      json: {
        error: {
          code:
            "invalid_field",

          detail:
            "provider detail"
        },

        meta: {
          request_id:
            "req-validation"
        }
      }
    });

  await assert.rejects(
    client.createTransaction(
      transactionBody()
    ),

    (error) => {
      assert.equal(
        error.code,
        "provider_validation_error"
      );

      assert.equal(
        error.requestId,
        "req-validation"
      );

      assert.equal(
        error.retryable,
        false
      );

      assert.doesNotMatch(
        error.message,
        /provider detail/
      );

      return true;
    }
  );
});

test("401 maps to provider authentication failure", async () => {
  const client =
    clientWith({
      status: 401,

      json: {
        error: {
          code:
            "invalid_token"
        },

        meta: {
          request_id:
            "req-auth"
        }
      }
    });

  await assert.rejects(
    client.createTransaction(
      transactionBody()
    ),

    (error) =>
      error.code ===
        "provider_auth_error" &&
      error.status === 401
  );
});

test("429 is classified but never automatically retryable", async () => {
  const client =
    clientWith({
      status: 429,

      json: {
        error: {
          code:
            "rate_limit_exceeded"
        },

        meta: {
          request_id:
            "req-rate"
        }
      }
    });

  await assert.rejects(
    client.createTransaction(
      transactionBody()
    ),

    (error) =>
      error.code ===
        "provider_rate_limited" &&
      error.retryable ===
        false
  );
});

test("5xx is classified but transaction create is not automatically retried", async () => {
  const client =
    clientWith({
      status: 503,

      json: {
        error: {
          code:
            "service_unavailable"
        },

        meta: {
          request_id:
            "req-503"
        }
      }
    });

  await assert.rejects(
    client.createTransaction(
      transactionBody()
    ),

    (error) =>
      error.code ===
        "provider_unavailable" &&
      error.retryable ===
        false
  );
});

test("client performs exactly one create attempt", async () => {
  let attempts = 0;

  const client =
    createPaddleApiClient({
      apiKey:
        sandboxKey,

      environment:
        "sandbox",

      transport: {
        async send() {
          attempts += 1;

          return {
            status: 503,

            json: {
              error: {
                code:
                  "service_unavailable"
              }
            }
          };
        }
      }
    });

  await assert.rejects(
    client.createTransaction(
      transactionBody()
    )
  );

  assert.equal(
    attempts,
    1
  );
});

test("provider detail and API key are not exposed through error message", async () => {
  const client =
    clientWith({
      status: 400,

      json: {
        error: {
          code:
            "invalid_field",

          detail:
            sandboxKey
        }
      }
    });

  await assert.rejects(
    client.createTransaction(
      transactionBody()
    ),

    (error) => {
      assert.doesNotMatch(
        error.message,
        /pdl_sdbx_apikey/
      );

      return true;
    }
  );
});