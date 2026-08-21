import assert from "node:assert/strict";
import test from "node:test";

import {
  CommercialCheckoutError,
  CommercialCheckoutOrchestrator
} from "../src/commercial-checkout-orchestrator.js";

import {
  createCommercialPriceCatalog
} from "../src/commercial-price-catalog.js";

const monthly =
  "pri_01h00000000000000000000000";

const annual =
  "pri_01h11111111111111111111111";

function setup({
  checkoutUrl =
    "https://checkout.paddle.com/txn",
  transactionId =
    "txn_01h00000000000000000000000"
} = {}) {
  const calls = [];

  const priceCatalog =
    createCommercialPriceCatalog({
      monthlyPriceId:
        monthly,

      annualPriceId:
        annual,

      maxSeats:
        20
    });

  const paddleClient = {
    async createTransaction(body) {
      calls.push(body);

      return {
        transactionId,
        status:
          "draft",

        collectionMode:
          "automatic",

        checkoutUrl,

        requestId:
          "req-1"
      };
    }
  };

  let nextId = 0;

  const orchestrator =
    new CommercialCheckoutOrchestrator({
      priceCatalog,
      paddleClient,

      idFactory(prefix) {
        nextId += 1;

        return `${prefix}-${nextId}`;
      }
    });

  return {
    calls,
    orchestrator
  };
}

test("trusted monthly checkout creates exactly one Paddle transaction", async () => {
  const {
    calls,
    orchestrator
  } = setup();

  const result =
    await orchestrator
      .createCheckout({
        accountId:
          "acct-trusted-1",

        plan:
          "monthly",

        seats:
          3
      });

  assert.equal(
    calls.length,
    1
  );

  assert.equal(
    calls[0].items[0].price_id,
    monthly
  );

  assert.equal(
    calls[0].items[0].quantity,
    3
  );

  assert.equal(
    calls[0].collection_mode,
    "automatic"
  );

  assert.equal(
    result.entitlementActivated,
    false
  );
});

test("checkout correlation uses trusted internal identities", async () => {
  const {
    calls,
    orchestrator
  } = setup();

  const result =
    await orchestrator
      .createCheckout({
        accountId:
          "acct-trusted-2",

        plan:
          "annual",

        seats:
          1
      });

  assert.equal(
    calls[0]
      .custom_data
      .pcspa_account_id,
    "acct-trusted-2"
  );

  assert.equal(
    calls[0]
      .custom_data
      .pcspa_subscription_id,
    result.subscriptionId
  );

  assert.equal(
    calls[0]
      .custom_data
      .pcspa_product,
    "pcspa-pro"
  );
});

test("caller never supplies provider price directly", async () => {
  const {
    calls,
    orchestrator
  } = setup();

  await orchestrator
    .createCheckout({
      accountId:
        "acct-trusted-3",

      plan:
        "monthly",

      seats:
        1,

      priceId:
        annual
    });

  assert.equal(
    calls[0].items[0].price_id,
    monthly
  );
});

test("unsupported plan never reaches Paddle client", async () => {
  const {
    calls,
    orchestrator
  } = setup();

  await assert.rejects(
    orchestrator
      .createCheckout({
        accountId:
          "acct-trusted-4",

        plan:
          "lifetime",

        seats:
          1
      })
  );

  assert.equal(
    calls.length,
    0
  );
});

test("invalid seat quantity never reaches Paddle client", async () => {
  const {
    calls,
    orchestrator
  } = setup();

  await assert.rejects(
    orchestrator
      .createCheckout({
        accountId:
          "acct-trusted-5",

        plan:
          "monthly",

        seats:
          1000
      })
  );

  assert.equal(
    calls.length,
    0
  );
});

test("insecure checkout URL fails closed", async () => {
  const {
    orchestrator
  } = setup({
    checkoutUrl:
      "http://checkout.example.test"
  });

  await assert.rejects(
    orchestrator
      .createCheckout({
        accountId:
          "acct-trusted-6",

        plan:
          "monthly",

        seats:
          1
      }),

    (error) =>
      error instanceof
        CommercialCheckoutError &&
      error.code ===
        "invalid_checkout_url"
  );
});

test("missing checkout URL does not activate entitlement", async () => {
  const {
    orchestrator
  } = setup({
    checkoutUrl:
      null
  });

  await assert.rejects(
    orchestrator
      .createCheckout({
        accountId:
          "acct-trusted-7",

        plan:
          "annual",

        seats:
          1
      }),

    (error) =>
      error.code ===
        "checkout_not_ready"
  );
});

test("checkout layer does not contain licensing mutation dependency", () => {
  const source =
    CommercialCheckoutOrchestrator
      .toString();

  assert.doesNotMatch(
    source,
    /entitlementStore|licensingStore|activateLicense/
  );
});