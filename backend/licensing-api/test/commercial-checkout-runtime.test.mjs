import assert from "node:assert/strict";
import test from "node:test";

import {
  createCommercialCheckoutRuntime
} from "../src/commercial-checkout-runtime.js";

const monthlyPriceId =
  "pri_01h00000000000000000000000";

const annualPriceId =
  "pri_01h11111111111111111111111";

const transactionId =
  "txn_01h00000000000000000000000";

function checkoutRequest(
  body = {},
  method = "POST"
) {
  return new Request(
    "https://getpcspa.com/api/commercial/checkout",
    {
      method,
      headers: {
        "content-type":
          "application/json"
      },

      body:
        method === "POST"
          ? JSON.stringify({
              billingInterval:
                "monthly",

              seats:
                1,

              ...body
            })
          : undefined
    }
  );
}

function setup({
  account = {
    accountId:
      "acct-trusted-1",

    productId:
      "pcspa-pro"
  },

  providerFailure =
    null
} = {}) {
  const paddleCalls = [];

  let ids = 0;

  const handler =
    createCommercialCheckoutRuntime({
      async resolveAuthenticatedAccount() {
        return account;
      },

      paddleClient: {
        async createTransaction(body) {
          paddleCalls.push(body);

          if (providerFailure) {
            throw providerFailure;
          }

          return {
            transactionId,

            status:
              "draft",

            collectionMode:
              "automatic",

            checkoutUrl:
              "https://checkout.paddle.test/pay?_ptxn=txn_test",

            requestId:
              "req-test"
          };
        }
      },

      monthlyPriceId,
      annualPriceId,

      productId:
        "pcspa-pro",

      maxSeats:
        25,

      idFactory(prefix) {
        ids += 1;

        return `${prefix}-${ids}`;
      }
    });

  return {
    handler,
    paddleCalls
  };
}

test(
  "full authenticated monthly checkout reaches Paddle exactly once",
  async () => {
    const {
      handler,
      paddleCalls
    } = setup();

    const response =
      await handler(
        checkoutRequest({
          billingInterval:
            "monthly",

          seats:
            3
        })
      );

    assert.equal(
      response.status,
      200
    );

    assert.equal(
      paddleCalls.length,
      1
    );

    assert.equal(
      paddleCalls[0]
        .items[0]
        .price_id,
      monthlyPriceId
    );

    assert.equal(
      paddleCalls[0]
        .items[0]
        .quantity,
      3
    );

    assert.equal(
      paddleCalls[0]
        .collection_mode,
      "automatic"
    );

    const result =
      await response.json();

    assert.equal(
      result.checkoutUrl,
      "https://checkout.paddle.test/pay?_ptxn=txn_test"
    );
  }
);

test(
  "annual checkout resolves annual server price",
  async () => {
    const {
      handler,
      paddleCalls
    } = setup();

    await handler(
      checkoutRequest({
        billingInterval:
          "annual",

        seats:
          5
      })
    );

    assert.equal(
      paddleCalls.length,
      1
    );

    assert.equal(
      paddleCalls[0]
        .items[0]
        .price_id,
      annualPriceId
    );

    assert.equal(
      paddleCalls[0]
        .items[0]
        .quantity,
      5
    );
  }
);

test(
  "request-controlled account and provider price cannot escape trusted boundaries",
  async () => {
    const {
      handler,
      paddleCalls
    } = setup();

    await handler(
      checkoutRequest({
        accountId:
          "acct-attacker",

        priceId:
          annualPriceId,

        billingInterval:
          "monthly",

        seats:
          1
      })
    );

    assert.equal(
      paddleCalls.length,
      1
    );

    assert.equal(
      paddleCalls[0]
        .items[0]
        .price_id,
      monthlyPriceId
    );

    assert.equal(
      paddleCalls[0]
        .custom_data
        .pcspa_account_id,
      "acct-trusted-1"
    );

    assert.notEqual(
      paddleCalls[0]
        .custom_data
        .pcspa_account_id,
      "acct-attacker"
    );
  }
);

test(
  "unauthenticated account never reaches Paddle",
  async () => {
    const {
      handler,
      paddleCalls
    } = setup({
      account: null
    });

    const response =
      await handler(
        checkoutRequest()
      );

    assert.equal(
      response.status,
      401
    );

    assert.equal(
      paddleCalls.length,
      0
    );
  }
);

test(
  "invalid seat quantity is a client error and never reaches Paddle",
  async () => {
    const {
      handler,
      paddleCalls
    } = setup();

    const response =
      await handler(
        checkoutRequest({
          seats:
            999
        })
      );

    assert.equal(
      response.status,
      400
    );

    assert.equal(
      paddleCalls.length,
      0
    );

    assert.deepEqual(
      await response.json(),
      {
        error:
          "invalid_checkout_request"
      }
    );
  }
);

test(
  "wrong trusted product is rejected before Paddle",
  async () => {
    const {
      handler,
      paddleCalls
    } = setup({
      account: {
        accountId:
          "acct-trusted-2",

        productId:
          "another-product"
      }
    });

    const response =
      await handler(
        checkoutRequest()
      );

    assert.equal(
      response.status,
      400
    );

    assert.equal(
      paddleCalls.length,
      0
    );
  }
);

test(
  "provider failure is sanitized by the public boundary",
  async () => {
    const providerFailure =
      new Error(
        "secret Paddle implementation detail"
      );

    providerFailure.code =
      "provider_unavailable";

    const {
      handler,
      paddleCalls
    } = setup({
      providerFailure
    });

    const response =
      await handler(
        checkoutRequest()
      );

    assert.equal(
      paddleCalls.length,
      1
    );

    assert.equal(
      response.status,
      502
    );

    const text =
      await response.text();

    assert.equal(
      text.includes(
        "secret Paddle implementation detail"
      ),
      false
    );

    assert.deepEqual(
      JSON.parse(text),
      {
        error:
          "checkout_unavailable"
      }
    );
  }
);