import {
  createPaddleHttpTransport
} from "./paddle-http-transport.js";

import {
  createPaddleApiClient
} from "./paddle-api-client.js";

import {
  createCommercialCheckoutRuntime
} from "./commercial-checkout-runtime.js";

import {
  createPaddleSandboxBillingProfileClient
} from "./paddle-sandbox-billing-profile-client.js";

export function createPaddleSandboxCheckoutRuntime({
  env,
  fetchImpl,
  resolveAuthenticatedAccount,
  resolveTrustedBillingProfile,
  idFactory
} = {}) {
  if (!env || typeof env !== "object") {
    throw new TypeError("env is required.");
  }

  if (env.PADDLE_ENVIRONMENT !== "sandbox") {
    throw new TypeError(
      "PADDLE_ENVIRONMENT must be sandbox."
    );
  }

  if (
    typeof env.PADDLE_SANDBOX_API_KEY !== "string" ||
    !env.PADDLE_SANDBOX_API_KEY.startsWith(
      "pdl_sdbx_apikey_"
    )
  ) {
    throw new TypeError(
      "Valid Paddle sandbox API key is required."
    );
  }

  if (typeof fetchImpl !== "function") {
    throw new TypeError("fetchImpl is required.");
  }

  if (
    typeof resolveAuthenticatedAccount !== "function"
  ) {
    throw new TypeError(
      "resolveAuthenticatedAccount is required."
    );
  }

  if (
    typeof resolveTrustedBillingProfile !== "function"
  ) {
    throw new TypeError(
      "resolveTrustedBillingProfile is required."
    );
  }

  if (typeof idFactory !== "function") {
    throw new TypeError("idFactory is required.");
  }

  const maxSeats =
    Number(env.PCSPA_MAX_SEATS);

  if (
    !Number.isSafeInteger(maxSeats) ||
    maxSeats < 1 ||
    maxSeats > 100000
  ) {
    throw new TypeError(
      "PCSPA_MAX_SEATS is invalid."
    );
  }

  const transport =
    createPaddleHttpTransport({
      fetchImpl
    });

  const paddleClient =
    createPaddleApiClient({
      apiKey:
        env.PADDLE_SANDBOX_API_KEY,

      environment: "sandbox",

      transport
    });

  const billingProfileClient =
    createPaddleSandboxBillingProfileClient({
      apiKey:
        env.PADDLE_SANDBOX_API_KEY,

      fetchImpl
    });

  async function resolveProviderBillingContext({
    accountId,
    productId
  }) {
    const profile =
      await resolveTrustedBillingProfile({
        accountId,
        productId
      });

    if (
      !profile ||
      typeof profile !== "object"
    ) {
      throw new TypeError(
        "Trusted billing profile is unavailable."
      );
    }

    return billingProfileClient
      .createBillingProfile({
        email:
          profile.email,

        name:
          profile.name,

        countryCode:
          profile.countryCode,

        postalCode:
          profile.postalCode
      });
  }

  return createCommercialCheckoutRuntime({
    resolveAuthenticatedAccount,

    resolveProviderBillingContext,

    paddleClient,

    monthlyPriceId:
      env.PADDLE_MONTHLY_PRICE_ID,

    annualPriceId:
      env.PADDLE_ANNUAL_PRICE_ID,

    productId: "pcspa-pro",

    maxSeats,

    idFactory
  });
}
