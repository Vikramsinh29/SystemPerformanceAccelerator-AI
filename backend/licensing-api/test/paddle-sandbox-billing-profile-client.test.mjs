import test from "node:test";
import assert from "node:assert/strict";

import {
  createPaddleSandboxBillingProfileClient
} from "../src/paddle-sandbox-billing-profile-client.js";

const API_KEY =
  "pdl_sdbx_apikey_test_only_123456789";

const CUSTOMER_ID =
  "ctm_01m0kbea1yqw8bfrfskhr11e2x";

const ADDRESS_ID =
  "add_01m0kbh51wyj0x0nbkv7bxdnd6";

function response(status, payload) {
  return {
    status,
    async text() {
      return JSON.stringify(payload);
    }
  };
}

test(
  "creates sandbox customer before country address",
  async () => {
    const calls = [];

    const client =
      createPaddleSandboxBillingProfileClient({
        apiKey: API_KEY,

        fetchImpl:
          async (url, options) => {
            calls.push({
              url,
              options
            });

            if (calls.length === 1) {
              return response(
                201,
                {
                  data: {
                    id: CUSTOMER_ID
                  }
                }
              );
            }

            return response(
              201,
              {
                data: {
                  id: ADDRESS_ID
                }
              }
            );
          }
      });

    const result =
      await client.createBillingProfile({
        email:
          "india-test@example.com",

        name:
          "PC-SPA India Test",

        countryCode:
          "IN",

        postalCode:
          "110001"
      });

    assert.deepEqual(
      result,
      {
        customerId:
          CUSTOMER_ID,

        addressId:
          ADDRESS_ID
      }
    );

    assert.equal(
      calls.length,
      2
    );

    assert.equal(
      calls[0].url,
      "https://sandbox-api.paddle.com/customers"
    );

    assert.equal(
      calls[1].url,
      `https://sandbox-api.paddle.com/customers/${CUSTOMER_ID}/addresses`
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
  }
);

test(
  "sandbox billing profile client refuses live key",
  () => {
    assert.throws(
      () =>
        createPaddleSandboxBillingProfileClient({
          apiKey:
            "pdl_live_apikey_forbidden_123456",

          fetchImpl:
            async () => {}
        }),
      /sandbox API key/
    );
  }
);

test(
  "billing profile requires ISO country code",
  async () => {
    const client =
      createPaddleSandboxBillingProfileClient({
        apiKey: API_KEY,

        fetchImpl:
          async () => {
            throw new Error(
              "network must not be reached"
            );
          }
      });

    await assert.rejects(
      () =>
        client.createBillingProfile({
          email:
            "india-test@example.com",

          name:
            "PC-SPA India Test",

          countryCode:
            "India",

          postalCode:
            "110001"
        }),
      /ISO-3166/
    );
  }
);

test(
  "billing profile requires postal code",
  async () => {
    const client =
      createPaddleSandboxBillingProfileClient({
        apiKey: API_KEY,

        fetchImpl:
          async () => {
            throw new Error(
              "network must not be reached"
            );
          }
      });

    await assert.rejects(
      () =>
        client.createBillingProfile({
          email:
            "india-test@example.com",

          name:
            "PC-SPA India Test",

          countryCode:
            "IN",

          postalCode:
            ""
        }),
      /postalCode/
    );
  }
);

test(
  "invalid Paddle customer fails before address creation",
  async () => {
    let calls = 0;

    const client =
      createPaddleSandboxBillingProfileClient({
        apiKey: API_KEY,

        fetchImpl:
          async () => {
            calls += 1;

            return response(
              201,
              {
                data: {
                  id: "invalid_customer"
                }
              }
            );
          }
      });

    await assert.rejects(
      () =>
        client.createBillingProfile({
          email:
            "india-test@example.com",

          name:
            "PC-SPA India Test",

          countryCode:
            "IN",

          postalCode:
            "110001"
        }),
      /invalid customer/
    );

    assert.equal(
      calls,
      1
    );
  }
);

test(
  "billing profile result never exposes API key",
  async () => {
    const client =
      createPaddleSandboxBillingProfileClient({
        apiKey: API_KEY,

        fetchImpl:
          async (url) => {
            if (
              url.endsWith(
                "/customers"
              )
            ) {
              return response(
                201,
                {
                  data: {
                    id: CUSTOMER_ID
                  }
                }
              );
            }

            return response(
              201,
              {
                data: {
                  id: ADDRESS_ID
                }
              }
            );
          }
      });

    const result =
      await client.createBillingProfile({
        email:
          "india-test@example.com",

        name:
          "PC-SPA India Test",

        countryCode:
          "IN",

        postalCode:
          "110001"
      });

    assert.equal(
      JSON.stringify(result)
        .includes(API_KEY),
      false
    );
  }
);
