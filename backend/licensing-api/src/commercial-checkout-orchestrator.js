import {
  buildPaddleTransactionRequest
} from "./paddle-transaction-request.js";

export class CommercialCheckoutOrchestrator {
  constructor({
    priceCatalog,
    paddleClient,
    idFactory
  }) {
    if (
      !priceCatalog ||
      typeof priceCatalog.resolve !== "function"
    ) {
      throw new CommercialCheckoutError(
        "invalid_price_catalog",
        "Commercial price catalog is required."
      );
    }

    if (
      !paddleClient ||
      typeof paddleClient.createTransaction !==
        "function"
    ) {
      throw new CommercialCheckoutError(
        "invalid_paddle_client",
        "Paddle client is required."
      );
    }

    if (typeof idFactory !== "function") {
      throw new CommercialCheckoutError(
        "invalid_id_factory",
        "Internal ID factory is required."
      );
    }

    this.priceCatalog =
      priceCatalog;

    this.paddleClient =
      paddleClient;

    this.idFactory =
      idFactory;
  }

  async createCheckout({
    accountId,
    plan,
    seats
  }) {
    requireTrustedAccountId(
      accountId
    );

    const commercial =
      this.priceCatalog.resolve(
        plan,
        seats
      );

    const subscriptionId =
      this.idFactory(
        "subscription"
      );

    requireInternalId(
      subscriptionId,
      "subscriptionId"
    );

    const transactionBody =
      buildPaddleTransactionRequest({
        priceId:
          commercial.providerPriceId,

        quantity:
          commercial.seats,

        internalAccountId:
          accountId,

        internalSubscriptionId:
          subscriptionId,

        productCode:
          commercial.productId
      });

    const provider =
      await this.paddleClient
        .createTransaction(
          transactionBody
        );

    if (
      !provider ||
      typeof provider !== "object"
    ) {
      throw new CommercialCheckoutError(
        "invalid_provider_result",
        "Payment provider returned an invalid checkout result."
      );
    }

    if (
      typeof provider.transactionId !== "string" ||
      !provider.transactionId.startsWith("txn_")
    ) {
      throw new CommercialCheckoutError(
        "invalid_provider_transaction",
        "Payment provider returned an invalid transaction."
      );
    }

    if (
      typeof provider.checkoutUrl !== "string"
    ) {
      throw new CommercialCheckoutError(
        "checkout_not_ready",
        "Payment checkout is not ready."
      );
    }

    let checkoutUrl;

    try {
      checkoutUrl =
        new URL(
          provider.checkoutUrl
        );
    } catch {
      throw new CommercialCheckoutError(
        "invalid_checkout_url",
        "Payment provider returned an invalid checkout URL."
      );
    }

    if (
      checkoutUrl.protocol !== "https:"
    ) {
      throw new CommercialCheckoutError(
        "invalid_checkout_url",
        "Payment checkout must use HTTPS."
      );
    }

    return Object.freeze({
      subscriptionId,

      plan:
        commercial.billingInterval,

      seats:
        commercial.seats,

      transactionId:
        provider.transactionId,

      checkoutUrl:
        checkoutUrl.toString(),

      requestId:
        provider.requestId ?? null,

      entitlementActivated:
        false
    });
  }
}

function requireTrustedAccountId(
  value
) {
  requireInternalId(
    value,
    "accountId"
  );
}

function requireInternalId(
  value,
  field
) {
  if (
    typeof value !== "string" ||
    !/^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/
      .test(value)
  ) {
    throw new CommercialCheckoutError(
      "invalid_internal_id",
      `${field} is invalid.`
    );
  }
}

export class CommercialCheckoutError
  extends Error {
  constructor(code, message) {
    super(message);

    this.name =
      "CommercialCheckoutError";

    this.code = code;
  }
}