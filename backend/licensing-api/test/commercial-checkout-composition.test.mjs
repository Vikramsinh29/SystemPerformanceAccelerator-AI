import assert from "node:assert/strict";
import test from "node:test";

import {
  LicensingIdentityError
} from "../src/licensing-identity-bridge.js";

import {
  createCommercialCheckoutComposition
} from "../src/commercial-checkout-composition.js";

function request() {
  return new Request(
    "https://getpcspa.com/api/commercial/checkout",
    {
      method: "POST",
      headers: {
        "content-type":
          "application/json"
      },
      body: JSON.stringify({
        planCode:
          "pcspa-pro",
        billingInterval:
          "annual",
        seats:
          5,

        accountId:
          "attacker-account"
      })
    }
  );
}

test(
  "trusted identity is resolved before checkout adapter executes",
  async () => {
    const calls = [];

    const handler =
      createCommercialCheckoutComposition({
        identityBridge: {
          async resolve() {
            return {
              accountId:
                "acct-trusted-1",

              productId:
                "pc-spa"
            };
          }
        },

        async checkoutHttpAdapter(
          req,
          identity
        ) {
          calls.push({
            req,
            identity
          });

          return new Response(
            JSON.stringify({
              checkoutUrl:
                "https://checkout.example.test/pay"
            }),
            {
              status: 200,
              headers: {
                "content-type":
                  "application/json"
              }
            }
          );
        }
      });

    const response =
      await handler(
        request()
      );

    assert.equal(
      response.status,
      200
    );

    assert.equal(
      calls.length,
      1
    );

    assert.deepEqual(
      calls[0].identity,
      {
        authenticated: true,
        accountId:
          "acct-trusted-1",
        productId:
          "pc-spa"
      }
    );
  }
);

test(
  "request-controlled account identity cannot override trusted identity",
  async () => {
    let observed;

    const handler =
      createCommercialCheckoutComposition({
        identityBridge: {
          async resolve() {
            return {
              accountId:
                "acct-server",
              productId:
                "pc-spa"
            };
          }
        },

        async checkoutHttpAdapter(
          req,
          identity
        ) {
          observed = identity;

          return new Response(
            "{}",
            {
              status: 200
            }
          );
        }
      });

    await handler(
      request()
    );

    assert.equal(
      observed.accountId,
      "acct-server"
    );

    assert.notEqual(
      observed.accountId,
      "attacker-account"
    );
  }
);

test(
  "unauthenticated identity is delegated as null to checkout boundary",
  async () => {
    let observed =
      "not-called";

    const handler =
      createCommercialCheckoutComposition({
        identityBridge: {
          async resolve() {
            throw new LicensingIdentityError(
              "unauthenticated"
            );
          }
        },

        async checkoutHttpAdapter(
          req,
          identity
        ) {
          observed =
            identity;

          return new Response(
            JSON.stringify({
              error:
                "unauthenticated"
            }),
            {
              status: 401,
              headers: {
                "content-type":
                  "application/json"
              }
            }
          );
        }
      });

    const response =
      await handler(
        request()
      );

    assert.equal(
      response.status,
      401
    );

    assert.equal(
      observed,
      null
    );
  }
);

test(
  "identity resolver failures fail closed before checkout execution",
  async () => {
    let checkoutCalls = 0;

    const handler =
      createCommercialCheckoutComposition({
        identityBridge: {
          async resolve() {
            throw new Error(
              "sensitive identity-provider failure"
            );
          }
        },

        async checkoutHttpAdapter() {
          checkoutCalls += 1;

          return new Response(
            "{}",
            {
              status: 200
            }
          );
        }
      });

    const response =
      await handler(
        request()
      );

    assert.equal(
      response.status,
      503
    );

    assert.equal(
      checkoutCalls,
      0
    );

    const text =
      await response.text();

    assert.equal(
      text.includes(
        "sensitive identity-provider failure"
      ),
      false
    );

    assert.deepEqual(
      JSON.parse(text),
      {
        error:
          "identity_unavailable"
      }
    );
  }
);

test(
  "composition does not resolve price or payment fields itself",
  async () => {
    let forwardedIdentity;

    const handler =
      createCommercialCheckoutComposition({
        identityBridge: {
          async resolve() {
            return {
              accountId:
                "acct-1",
              productId:
                "pc-spa"
            };
          }
        },

        async checkoutHttpAdapter(
          req,
          identity
        ) {
          forwardedIdentity =
            identity;

          return new Response(
            "{}",
            {
              status: 200
            }
          );
        }
      });

    await handler(
      request()
    );

    assert.equal(
      Object.hasOwn(
        forwardedIdentity,
        "priceId"
      ),
      false
    );

    assert.equal(
      Object.hasOwn(
        forwardedIdentity,
        "amountMinor"
      ),
      false
    );

    assert.equal(
      Object.hasOwn(
        forwardedIdentity,
        "currency"
      ),
      false
    );
  }
);

test(
  "composition refuses missing identity bridge",
  () => {
    assert.throws(
      () =>
        createCommercialCheckoutComposition({
          checkoutHttpAdapter() {}
        }),
      /identityBridge/
    );
  }
);

test(
  "composition refuses missing checkout adapter",
  () => {
    assert.throws(
      () =>
        createCommercialCheckoutComposition({
          identityBridge: {
            async resolve() {}
          }
        }),
      /checkoutHttpAdapter/
    );
  }
);