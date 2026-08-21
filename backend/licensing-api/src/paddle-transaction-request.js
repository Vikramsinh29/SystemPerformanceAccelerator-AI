const PRICE_ID_PATTERN =
  /^pri_[a-z\d]{26}$/;

const INTERNAL_ID_PATTERN =
  /^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/;

export function buildPaddleTransactionRequest({
  priceId,
  quantity = 1,
  internalAccountId,
  internalSubscriptionId,
  productCode = "pc-spa",
  checkoutUrl = null
}) {
  requirePriceId(priceId);
  requireQuantity(quantity);

  requireInternalId(
    internalAccountId,
    "internalAccountId"
  );

  requireInternalId(
    internalSubscriptionId,
    "internalSubscriptionId"
  );

  requireInternalId(
    productCode,
    "productCode"
  );

  const body = {
    items: [
      {
        price_id: priceId,
        quantity
      }
    ],

    collection_mode: "automatic",

    custom_data: {
      pcspa_account_id:
        internalAccountId,

      pcspa_subscription_id:
        internalSubscriptionId,

      pcspa_product:
        productCode
    }
  };

  if (checkoutUrl !== null) {
    body.checkout = {
      url: normalizeCheckoutUrl(
        checkoutUrl
      )
    };
  }

  return deepFreeze(body);
}

export function buildPaddleTransactionHttpRequest({
  apiKey,
  body,
  environment = "sandbox"
}) {
  validateApiKey(
    apiKey,
    environment
  );

  if (
    !body ||
    typeof body !== "object" ||
    Array.isArray(body)
  ) {
    throw new PaddleTransactionRequestError(
      "invalid_body",
      "Transaction body is required."
    );
  }

  const baseUrl =
    resolveBaseUrl(environment);

  return Object.freeze({
    method: "POST",

    url:
      `${baseUrl}/transactions`,

    headers: Object.freeze({
      Authorization:
        `Bearer ${apiKey}`,

      "Content-Type":
        "application/json",

      "Paddle-Version":
        "1"
    }),

    body:
      JSON.stringify(body)
  });
}

function resolveBaseUrl(environment) {
  if (environment === "sandbox") {
    return "https://sandbox-api.paddle.com";
  }

  if (environment === "live") {
    return "https://api.paddle.com";
  }

  throw new PaddleTransactionRequestError(
    "invalid_environment",
    "Paddle environment is invalid."
  );
}

function validateApiKey(
  apiKey,
  environment
) {
  if (
    typeof apiKey !== "string" ||
    apiKey.length < 20
  ) {
    throw new PaddleTransactionRequestError(
      "invalid_api_key",
      "A valid Paddle API key is required."
    );
  }

  if (
    environment === "sandbox" &&
    !apiKey.startsWith(
      "pdl_sdbx_apikey_"
    )
  ) {
    throw new PaddleTransactionRequestError(
      "environment_key_mismatch",
      "Sandbox requires a sandbox API key."
    );
  }

  if (
    environment === "live" &&
    !apiKey.startsWith(
      "pdl_live_apikey_"
    )
  ) {
    throw new PaddleTransactionRequestError(
      "environment_key_mismatch",
      "Live requires a live API key."
    );
  }
}

function requirePriceId(value) {
  if (
    typeof value !== "string" ||
    !PRICE_ID_PATTERN.test(value)
  ) {
    throw new PaddleTransactionRequestError(
      "invalid_price_id",
      "Paddle price ID is invalid."
    );
  }
}

function requireQuantity(value) {
  if (
    !Number.isSafeInteger(value) ||
    value < 1 ||
    value > 999999999
  ) {
    throw new PaddleTransactionRequestError(
      "invalid_quantity",
      "Transaction quantity is invalid."
    );
  }
}

function requireInternalId(
  value,
  field
) {
  if (
    typeof value !== "string" ||
    !INTERNAL_ID_PATTERN.test(value)
  ) {
    throw new PaddleTransactionRequestError(
      "invalid_internal_id",
      `${field} is invalid.`
    );
  }
}

function normalizeCheckoutUrl(value) {
  if (typeof value !== "string") {
    throw new PaddleTransactionRequestError(
      "invalid_checkout_url",
      "Checkout URL is invalid."
    );
  }

  let parsed;

  try {
    parsed = new URL(value);
  } catch {
    throw new PaddleTransactionRequestError(
      "invalid_checkout_url",
      "Checkout URL is invalid."
    );
  }

  if (parsed.protocol !== "https:") {
    throw new PaddleTransactionRequestError(
      "invalid_checkout_url",
      "Checkout URL must use HTTPS."
    );
  }

  return parsed.toString();
}

function deepFreeze(value) {
  if (
    value &&
    typeof value === "object"
  ) {
    Object.freeze(value);

    for (
      const nested
      of Object.values(value)
    ) {
      deepFreeze(nested);
    }
  }

  return value;
}

export class PaddleTransactionRequestError
  extends Error {
  constructor(code, message) {
    super(message);

    this.name =
      "PaddleTransactionRequestError";

    this.code = code;
  }
}