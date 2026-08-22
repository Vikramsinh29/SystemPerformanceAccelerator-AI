import {
  createLicensingIdentityBridge
} from "./licensing-identity-bridge.js";

import {
  createCommercialPriceCatalog
} from "./commercial-price-catalog.js";

import {
  CommercialCheckoutOrchestrator
} from "./commercial-checkout-orchestrator.js";

import {
  createCommercialCheckoutHttpAdapter
} from "./commercial-checkout-http-adapter.js";

import {
  createCommercialCheckoutComposition
} from "./commercial-checkout-composition.js";

export function createCommercialCheckoutRuntime({
  resolveAuthenticatedAccount,

  paddleClient,

  monthlyPriceId,
  annualPriceId,

  productId = "pcspa-pro",

  maxSeats = 100,

  idFactory,

  resolveProviderBillingContext = null
} = {}) {
  if (
    typeof resolveAuthenticatedAccount !== "function"
  ) {
    throw new TypeError(
      "resolveAuthenticatedAccount is required."
    );
  }

  if (
    !paddleClient ||
    typeof paddleClient.createTransaction !== "function"
  ) {
    throw new TypeError(
      "paddleClient.createTransaction is required."
    );
  }

  if (typeof idFactory !== "function") {
    throw new TypeError(
      "idFactory is required."
    );
  }

  if (
    resolveProviderBillingContext !== null &&
    typeof resolveProviderBillingContext !== "function"
  ) {
    throw new TypeError(
      "resolveProviderBillingContext must be a function."
    );
  }

  const identityBridge =
    createLicensingIdentityBridge({
      resolveAuthenticatedAccount,
      productId
    });

  const priceCatalog =
    createCommercialPriceCatalog({
      productId,
      monthlyPriceId,
      annualPriceId,
      maxSeats
    });

  const checkoutOrchestrator =
    new CommercialCheckoutOrchestrator({
      priceCatalog,
      paddleClient,
      idFactory
    });

  /*
   * Product authorization remains part of the trusted
   * server identity boundary. A request cannot switch
   * products through its JSON body.
   */
  const productBoundCheckout =
    Object.freeze({
      async createCheckout(command) {
        if (
          command.productId !== productId
        ) {
          const error =
            new Error(
              "Trusted product is not authorized for this checkout."
            );

          error.code =
            "invalid_checkout_request";

          throw error;
        }

        let providerBillingContext = null;

        if (resolveProviderBillingContext !== null) {
          providerBillingContext =
            await resolveProviderBillingContext({
              accountId:
                command.accountId,

              productId
            });

          if (
            !providerBillingContext ||
            typeof providerBillingContext !== "object"
          ) {
            const error =
              new Error(
                "Trusted provider billing context is unavailable."
              );

            error.code =
              "checkout_unavailable";

            throw error;
          }
        }

        return checkoutOrchestrator
          .createCheckout({
            accountId:
              command.accountId,

            plan:
              command.plan,

            seats:
              command.seats,

            providerCustomerId:
              providerBillingContext?.customerId ?? null,

            providerAddressId:
              providerBillingContext?.addressId ?? null
          });
      }
    });

  const checkoutHttpAdapter =
    createCommercialCheckoutHttpAdapter({
      checkoutOrchestrator:
        productBoundCheckout
    });

  return createCommercialCheckoutComposition({
    identityBridge,
    checkoutHttpAdapter
  });
}
