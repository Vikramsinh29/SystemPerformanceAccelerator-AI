import test from "node:test";
import assert from "node:assert/strict";

import {
  createCommercialCheckoutRuntime
} from "../src/commercial-checkout-runtime.js";

const CUSTOMER =
  "ctm_01m0kbea1yqw8bfrfskhr11e2x";

const ADDRESS =
  "add_01m0kbh51wyj0x0nbkv7bxdnd6";

test(
  "runtime resolves trusted provider billing context server-side",
  async () => {
    let capturedBody = null;

    const runtime =
      createCommercialCheckoutRuntime({
        resolveAuthenticatedAccount:
          async () => ({
            accountId:
              "acct_test_001",
            productId:
              "pcspa-pro"
          }),

        resolveProviderBillingContext:
          async ({ accountId, productId }) => {
            assert.equal(
              accountId,
              "acct_test_001"
            );

            assert.equal(
              productId,
              "pcspa-pro"
            );

            return {
              customerId:
                CUSTOMER,
              addressId:
                ADDRESS
            };
          },

        paddleClient: {
          async createTransaction(body) {
            capturedBody = body;

            return {
              transactionId:
                "txn_01m0kbh67p71v5129qcf3m0672",
              checkoutUrl:
                "https://localhost/?_ptxn=test",
              requestId:
                "req_test"
            };
          }
        },

        monthlyPriceId:
          "pri_01m0k4ycnmg10qe9455hwdx0vr",

        annualPriceId:
          "pri_01m0k52c9eqvxe15bktrhaee3x",

        maxSeats:
          999,

        idFactory:
          () => "sub_internal_test_001"
      });

    const response =
      await runtime(
        new Request(
          "https://example.test/checkout",
          {
            method: "POST",
            headers: {
              "content-type":
                "application/json"
            },
            body: JSON.stringify({
              billingInterval:
                "monthly",
              seats:
                1
            })
          }
        )
      );

    assert.equal(
      response.status,
      200
    );

    assert.equal(
      capturedBody.customer_id,
      CUSTOMER
    );

    assert.equal(
      capturedBody.address_id,
      ADDRESS
    );
  }
);
