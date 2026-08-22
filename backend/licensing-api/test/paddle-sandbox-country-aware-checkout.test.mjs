import test from "node:test";
import assert from "node:assert/strict";

import {
  createPaddleSandboxCheckoutRuntime
} from "../src/paddle-sandbox-checkout-runtime.js";

const MONTHLY_PRICE_ID =
  "pri_01m0k4ycnmg10qe9455hwdx0vr";

const ANNUAL_PRICE_ID =
  "pri_01m0k52c9eqvxe15bktrhaee3x";

const CUSTOMER_ID =
  "ctm_01m0kbea1yqw8bfrfskhr11e2x";

const ADDRESS_ID =
  "add_01m0kbh51wyj0x0nbkv7bxdnd6";

const TRANSACTION_ID =
  "txn_01m0kbh67p71v5129qcf3m0672";

test(
  "sandbox checkout uses trusted India billing profile before transaction",
  async () => {
    const calls = [];

    const fetchImpl =
      async (url, options) => {
        calls.push({
          url: String(url),
          options
        });

        if (
          String(url) ===
          "https://sandbox-api.paddle.com/customers"
        ) {
          return new Response(
            JSON.stringify({
              data: {
                id: CUSTOMER_ID
              }
            }),
            {
              status: 201,
              headers: {
                "content-type":
                  "application/json"
              }
            }
          );
        }

        if (
          String(url) ===
          `https://sandbox-api.paddle.com/customers/${CUSTOMER_ID}/addresses`
        ) {
          return new Response(
            JSON.stringify({
              data: {
                id: ADDRESS_ID
              }
            }),
            {
              status: 201,
              headers: {
                "content-type":
                  "application/json"
              }
            }
          );
        }

        if (
          String(url) ===
          "https://sandbox-api.paddle.com/transactions"
        ) {
          return new Response(
            JSON.stringify({
              data: {
                id:
                  TRANSACTION_ID,

                status:
                  "ready",

                collection_mode:
                  "automatic",

                checkout: {
                  url:
                    `https://localhost/?_ptxn=${TRANSACTION_ID}`
                }
              },

              meta: {
                request_id:
                  "req_sandbox_test"
              }
            }),
            {
              status: 201,
              headers: {
                "content-type":
                  "application/json"
              }
            }
          );
        }

        throw new Error(
          `Unexpected URL: ${url}`
        );
      };

    const runtime =
      createPaddleSandboxCheckoutRuntime({
        env: {
          PADDLE_ENVIRONMENT:
            "sandbox",

          PADDLE_SANDBOX_API_KEY:
            "pdl_sdbx_apikey_test_only",

          PADDLE_MONTHLY_PRICE_ID:
            MONTHLY_PRICE_ID,

          PADDLE_ANNUAL_PRICE_ID:
            ANNUAL_PRICE_ID,

          PCSPA_MAX_SEATS:
            "999"
        },

        fetchImpl,

        resolveAuthenticatedAccount:
          async () => ({
            accountId:
              "acct_test_001",

            productId:
              "pcspa-pro"
          }),

        resolveTrustedBillingProfile:
          async ({
            accountId,
            productId
          }) => {
            assert.equal(
              accountId,
              "acct_test_001"
            );

            assert.equal(
              productId,
              "pcspa-pro"
            );

            return {
              email:
                "india-test@example.com",

              name:
                "PC-SPA India Test",

              countryCode:
                "IN",

              postalCode:
                "110001"
            };
          },

        idFactory:
          () =>
            "sub_internal_test_001"
      });

    const response =
      await runtime(
        new Request(
          "https://example.test/checkout",
          {
            method:
              "POST",

            headers: {
              "content-type":
                "application/json"
            },

            body:
              JSON.stringify({
                billingInterval:
                  "monthly",

                seats:
                  1,

                priceId:
                  "pri_attacker_controlled",

                customer_id:
                  "ctm_attacker_controlled",

                address_id:
                  "add_attacker_controlled",

                currency:
                  "USD",

                amount:
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
      calls.length,
      3
    );

    assert.equal(
      calls[0].url,
      "https://sandbox-api.paddle.com/customers"
    );

    assert.equal(
      calls[1].url,
      `https://sandbox-api.paddle.com/customers/${CUSTOMER_ID}/addresses`
    );

    assert.equal(
      calls[2].url,
      "https://sandbox-api.paddle.com/transactions"
    );

    const customerBody =
      JSON.parse(
        calls[0].options.body
      );

    assert.equal(
      customerBody.email,
      "india-test@example.com"
    );

    const addressBody =
      JSON.parse(
        calls[1].options.body
      );

    assert.equal(
      addressBody.country_code,
      "IN"
    );

    assert.equal(
      addressBody.postal_code,
      "110001"
    );

    const transactionBody =
      JSON.parse(
        calls[2].options.body
      );

    assert.equal(
      transactionBody.items[0].price_id,
      MONTHLY_PRICE_ID
    );

    assert.equal(
      transactionBody.items[0].quantity,
      1
    );

    assert.equal(
      transactionBody.customer_id,
      CUSTOMER_ID
    );

    assert.equal(
      transactionBody.address_id,
      ADDRESS_ID
    );

    assert.equal(
      transactionBody.currency_code,
      undefined
    );

    assert.equal(
      transactionBody.amount,
      undefined
    );

    assert.equal(
      transactionBody.custom_data
        .pcspa_account_id,
      "acct_test_001"
    );

    const publicResult =
      await response.json();

    assert.equal(
      publicResult.checkoutUrl,
      `https://localhost/?_ptxn=${TRANSACTION_ID}`
    );
  }
);
