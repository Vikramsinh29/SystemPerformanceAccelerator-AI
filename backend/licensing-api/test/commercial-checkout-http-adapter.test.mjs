import assert from "node:assert/strict";
import test from "node:test";

import {
  createCommercialCheckoutHttpAdapter
} from "../src/commercial-checkout-http-adapter.js";

function request(body, options = {}) {
  return new Request(
    "https://licensing.internal/v1/commercial/checkout",
    {
      method: options.method ?? "POST",
      headers: {
        "content-type":
          options.contentType ?? "application/json"
      },
      body:
        (options.method ?? "POST") === "POST"
          ? body
          : undefined
    }
  );
}

function identity(overrides = {}) {
  return {
    authenticated: true,
    accountId: "acct_verified_001",
    productId: "pc-spa",
    ...overrides
  };
}

test(
  "authenticated checkout forwards only trusted identity and bounded commercial choices",
  async () => {
    let received = null;

    const handler =
      createCommercialCheckoutHttpAdapter({
        checkoutOrchestrator: {
          async createCheckout(command) {
            received = command;

            return {
              checkoutUrl:
                "https://checkout.example.test/pay?_ptxn=txn_test"
            };
          }
        }
      });

    const response = await handler(
      request(
        JSON.stringify({
          planCode: "pcspa-pro",
          billingInterval: "annual",
          seats: 5,

          accountId: "acct_attacker",
          productId: "evil-product",
          priceId: "pri_attacker",
          currency: "USD",
          amountMinor: 1,
          entitlementState: "active",
          providerTransactionId: "txn_fake"
        })
      ),
      identity()
    );

    assert.equal(response.status, 200);

    assert.deepEqual(
      received,
      {
        accountId: "acct_verified_001",
        productId: "pc-spa",
        plan: "annual",
        seats: 5
      }
    );

    const body = await response.json();

    assert.deepEqual(body, {
      checkoutUrl:
        "https://checkout.example.test/pay?_ptxn=txn_test"
    });
  }
);

test(
  "request-controlled account cannot override authenticated identity",
  async () => {
    let received;

    const handler =
      createCommercialCheckoutHttpAdapter({
        checkoutOrchestrator: {
          async createCheckout(command) {
            received = command;

            return {
              checkoutUrl:
                "https://checkout.example.test/pay"
            };
          }
        }
      });

    await handler(
      request(
        JSON.stringify({
          accountId: "acct_attacker",
          planCode: "pcspa-pro",
          billingInterval: "monthly",
          seats: 1
        })
      ),
      identity()
    );

    assert.equal(
      received.accountId,
      "acct_verified_001"
    );

    assert.notEqual(
      received.accountId,
      "acct_attacker"
    );
  }
);

test(
  "provider price and monetary fields never cross checkout boundary",
  async () => {
    let received;

    const handler =
      createCommercialCheckoutHttpAdapter({
        checkoutOrchestrator: {
          async createCheckout(command) {
            received = command;

            return {
              checkoutUrl:
                "https://checkout.example.test/pay"
            };
          }
        }
      });

    await handler(
      request(
        JSON.stringify({
          planCode: "pcspa-pro",
          billingInterval: "annual",
          seats: 10,
          priceId: "pri_fake",
          providerPriceId: "pri_fake_2",
          amountMinor: 1,
          currency: "XXX",
          provider: "attacker"
        })
      ),
      identity()
    );

    assert.equal(
      Object.hasOwn(received, "priceId"),
      false
    );

    assert.equal(
      Object.hasOwn(
        received,
        "providerPriceId"
      ),
      false
    );

    assert.equal(
      Object.hasOwn(received, "amountMinor"),
      false
    );

    assert.equal(
      Object.hasOwn(received, "currency"),
      false
    );

    assert.equal(
      Object.hasOwn(received, "provider"),
      false
    );
  }
);

test(
  "unauthenticated checkout fails before orchestrator execution",
  async () => {
    let calls = 0;

    const handler =
      createCommercialCheckoutHttpAdapter({
        checkoutOrchestrator: {
          async createCheckout() {
            calls += 1;

            return {
              checkoutUrl:
                "https://checkout.example.test/pay"
            };
          }
        }
      });

    const response = await handler(
      request(
        JSON.stringify({
          planCode: "pcspa-pro",
          billingInterval: "monthly",
          seats: 1
        })
      ),
      null
    );

    assert.equal(response.status, 401);
    assert.equal(calls, 0);

    assert.deepEqual(
      await response.json(),
      { error: "unauthenticated" }
    );
  }
);

test(
  "malformed JSON is rejected before checkout execution",
  async () => {
    let calls = 0;

    const handler =
      createCommercialCheckoutHttpAdapter({
        checkoutOrchestrator: {
          async createCheckout() {
            calls += 1;
          }
        }
      });

    const response = await handler(
      request("{not-json"),
      identity()
    );

    assert.equal(response.status, 400);
    assert.equal(calls, 0);

    assert.deepEqual(
      await response.json(),
      { error: "invalid_json" }
    );
  }
);

test(
  "checkout accepts POST only",
  async () => {
    const handler =
      createCommercialCheckoutHttpAdapter({
        checkoutOrchestrator: {
          async createCheckout() {
            throw new Error(
              "must not execute"
            );
          }
        }
      });

    const response = await handler(
      request("", { method: "GET" }),
      identity()
    );

    assert.equal(response.status, 405);
    assert.equal(
      response.headers.get("allow"),
      "POST"
    );
  }
);

test(
  "non-json checkout request is rejected",
  async () => {
    const handler =
      createCommercialCheckoutHttpAdapter({
        checkoutOrchestrator: {
          async createCheckout() {
            throw new Error(
              "must not execute"
            );
          }
        }
      });

    const response = await handler(
      request(
        "plan=pcspa-pro",
        {
          contentType:
            "application/x-www-form-urlencoded"
        }
      ),
      identity()
    );

    assert.equal(response.status, 400);
  }
);

test(
  "insecure checkout URL returned downstream is never exposed",
  async () => {
    const handler =
      createCommercialCheckoutHttpAdapter({
        checkoutOrchestrator: {
          async createCheckout() {
            return {
              checkoutUrl:
                "http://checkout.example.test/pay"
            };
          }
        }
      });

    const response = await handler(
      request(
        JSON.stringify({
          planCode: "pcspa-pro",
          billingInterval: "monthly",
          seats: 1
        })
      ),
      identity()
    );

    assert.equal(response.status, 502);

    assert.deepEqual(
      await response.json(),
      { error: "checkout_unavailable" }
    );
  }
);

test(
  "provider failures are sanitized at public checkout boundary",
  async () => {
    const handler =
      createCommercialCheckoutHttpAdapter({
        checkoutOrchestrator: {
          async createCheckout() {
            const error =
              new Error(
                "secret provider failure"
              );

            error.code =
              "provider_api_error";

            throw error;
          }
        }
      });

    const response = await handler(
      request(
        JSON.stringify({
          planCode: "pcspa-pro",
          billingInterval: "monthly",
          seats: 1
        })
      ),
      identity()
    );

    assert.equal(response.status, 502);

    const text = await response.text();

    assert.equal(
      text.includes(
        "secret provider failure"
      ),
      false
    );

    assert.deepEqual(
      JSON.parse(text),
      { error: "checkout_unavailable" }
    );
  }
);

test(
  "construction refuses missing checkout orchestrator",
  () => {
    assert.throws(
      () =>
        createCommercialCheckoutHttpAdapter(),
      /checkoutOrchestrator/
    );
  }
);
