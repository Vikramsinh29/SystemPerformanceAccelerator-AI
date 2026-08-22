import test from "node:test";
import assert from "node:assert/strict";

import {
  createPaddleSandboxCheckoutRuntime
} from "../src/paddle-sandbox-checkout-runtime.js";

const MONTHLY_PRICE_ID =
  "pri_01m0k4ycnmg10qe9455hwdx0vr";

const ANNUAL_PRICE_ID =
  "pri_01m0k52c9eqvxe15bktrhaee3x";

const SANDBOX_KEY =
  "pdl_sdbx_apikey_test_only";

function validEnv() {
  return {
    PADDLE_ENVIRONMENT: "sandbox",
    PADDLE_SANDBOX_API_KEY: SANDBOX_KEY,
    PADDLE_MONTHLY_PRICE_ID:
      MONTHLY_PRICE_ID,
    PADDLE_ANNUAL_PRICE_ID:
      ANNUAL_PRICE_ID,
    PCSPA_MAX_SEATS: "999"
  };
}

function validDependencies() {
  return {
    fetchImpl: async () => {
      throw new Error(
        "network must not be used during construction"
      );
    },

    resolveAuthenticatedAccount:
      async () => ({
        accountId: "acct_test_001",
        productId: "pcspa-pro"
      }),

    resolveTrustedBillingProfile:
      async () => ({
        email: "sandbox@example.com",
        name: "PC-SPA Sandbox",
        countryCode: "IN",
        postalCode: "110001"
      }),

    idFactory: () =>
      "sub_internal_test_001"
  };
}

test(
  "sandbox runtime composes without network access",
  () => {
    let networkCalls = 0;

    const runtime =
      createPaddleSandboxCheckoutRuntime({
        env: validEnv(),

        fetchImpl: async () => {
          networkCalls += 1;

          throw new Error(
            "unexpected network access"
          );
        },

        resolveAuthenticatedAccount:
          async () => ({
            accountId: "acct_test_001",
            productId: "pcspa-pro"
          }),

        resolveTrustedBillingProfile:
          async () => ({
            email: "sandbox@example.com",
            name: "PC-SPA Sandbox",
            countryCode: "IN",
            postalCode: "110001"
          }),

        idFactory: () =>
          "sub_internal_test_001"
      });

    assert.equal(
      typeof runtime,
      "function"
    );

    assert.equal(
      networkCalls,
      0
    );
  }
);

test(
  "sandbox runtime refuses live environment",
  () => {
    const env = validEnv();

    env.PADDLE_ENVIRONMENT =
      "live";

    assert.throws(
      () =>
        createPaddleSandboxCheckoutRuntime({
          env,
          ...validDependencies()
        }),
      /must be sandbox/
    );
  }
);

test(
  "sandbox runtime refuses non-sandbox API key",
  () => {
    const env = validEnv();

    env.PADDLE_SANDBOX_API_KEY =
      "pdl_live_apikey_forbidden";

    assert.throws(
      () =>
        createPaddleSandboxCheckoutRuntime({
          env,
          ...validDependencies()
        }),
      /sandbox API key/
    );
  }
);

test(
  "sandbox runtime refuses invalid seat limit",
  () => {
    const env = validEnv();

    env.PCSPA_MAX_SEATS =
      "0";

    assert.throws(
      () =>
        createPaddleSandboxCheckoutRuntime({
          env,
          ...validDependencies()
        }),
      /PCSPA_MAX_SEATS/
    );
  }
);

test(
  "sandbox runtime requires trusted identity resolver",
  () => {
    const dependencies =
      validDependencies();

    assert.throws(
      () =>
        createPaddleSandboxCheckoutRuntime({
          env: validEnv(),

          fetchImpl:
            dependencies.fetchImpl,

          idFactory:
            dependencies.idFactory
        }),
      /resolveAuthenticatedAccount/
    );
  }
);
