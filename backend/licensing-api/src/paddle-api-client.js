import {
  buildPaddleTransactionHttpRequest
} from "./paddle-transaction-request.js";

import {
  PaddleTransportError
} from "./paddle-http-transport.js";

const TRANSACTION_ID =
  /^txn_[a-z\d]{26}$/;

const ALLOWED_TRANSACTION_STATES =
  new Set([
    "draft",
    "ready",
    "billed",
    "paid",
    "completed",
    "canceled",
    "past_due"
  ]);

export class PaddleApiError extends Error {
  constructor(code, message, options = {}) {
    super(message);

    this.name = "PaddleApiError";
    this.code = code;
    this.status = options.status ?? null;
    this.requestId = options.requestId ?? null;
    this.retryable = options.retryable ?? false;
  }
}

export function createPaddleApiClient({
  apiKey,
  environment = "sandbox",
  transport
}) {
  if (
    !transport ||
    typeof transport.send !== "function"
  ) {
    throw new PaddleApiError(
      "invalid_transport",
      "Paddle transport is required."
    );
  }

  return Object.freeze({
    async createTransaction(body) {
      const request =
        buildPaddleTransactionHttpRequest({
          apiKey,
          body,
          environment
        });

      let response;

      try {
        response =
          await transport.send(request);
      } catch (error) {
        if (error instanceof PaddleTransportError) {
          throw new PaddleApiError(
            error.code,
            "Paddle transaction request failed.",
            {
              status: error.status,
              requestId: error.requestId,
              retryable: false
            }
          );
        }

        throw new PaddleApiError(
          "transport_failure",
          "Paddle transaction request failed.",
          {
            retryable: false
          }
        );
      }

      if (response.status !== 201) {
        throw mapFailure(response);
      }

      return validateCreatedTransaction(
        response.json
      );
    }
  });
}

function validateCreatedTransaction(payload) {
  if (
    !payload ||
    typeof payload !== "object" ||
    !payload.data ||
    typeof payload.data !== "object"
  ) {
    throw new PaddleApiError(
      "invalid_success_response",
      "Paddle returned an invalid transaction response."
    );
  }

  const transaction = payload.data;

  if (
    typeof transaction.id !== "string" ||
    !TRANSACTION_ID.test(transaction.id)
  ) {
    throw new PaddleApiError(
      "invalid_transaction_id",
      "Paddle returned an invalid transaction identifier."
    );
  }

  if (
    typeof transaction.status !== "string" ||
    !ALLOWED_TRANSACTION_STATES.has(
      transaction.status
    )
  ) {
    throw new PaddleApiError(
      "invalid_transaction_status",
      "Paddle returned an invalid transaction status."
    );
  }

  if (
    transaction.collection_mode !==
    "automatic"
  ) {
    throw new PaddleApiError(
      "unexpected_collection_mode",
      "Paddle returned an unexpected collection mode."
    );
  }

  const requestId =
    readRequestId(payload);

  return Object.freeze({
    transactionId:
      transaction.id,

    status:
      transaction.status,

    collectionMode:
      transaction.collection_mode,

    checkoutUrl:
      normalizeReturnedCheckoutUrl(
        transaction.checkout?.url
      ),

    requestId
  });
}

function mapFailure(response) {
  const payload =
    response.json;

  const requestId =
    readRequestId(payload);

  const providerCode =
    typeof payload?.error?.code === "string"
      ? payload.error.code
      : null;

  let code =
    "provider_error";

  if (response.status === 400) {
    code = "provider_validation_error";
  } else if (
    response.status === 401 ||
    response.status === 403
  ) {
    code = "provider_auth_error";
  } else if (response.status === 404) {
    code = "provider_not_found";
  } else if (response.status === 429) {
    code = "provider_rate_limited";
  } else if (response.status >= 500) {
    code = "provider_unavailable";
  }

  return new PaddleApiError(
    code,
    "Paddle rejected the transaction request.",
    {
      status: response.status,
      requestId,
      /*
       * Deliberately false for transaction creation.
       * The caller must reconcile before attempting
       * another create operation.
       */
      retryable: false,
      providerCode
    }
  );
}

function readRequestId(payload) {
  const value =
    payload?.meta?.request_id;

  if (
    typeof value !== "string" ||
    value.length < 1 ||
    value.length > 200
  ) {
    return null;
  }

  return value;
}

function normalizeReturnedCheckoutUrl(value) {
  if (value == null) {
    return null;
  }

  if (typeof value !== "string") {
    throw new PaddleApiError(
      "invalid_checkout_url",
      "Paddle returned an invalid checkout URL."
    );
  }

  let parsed;

  try {
    parsed = new URL(value);
  } catch {
    throw new PaddleApiError(
      "invalid_checkout_url",
      "Paddle returned an invalid checkout URL."
    );
  }

  if (parsed.protocol !== "https:") {
    throw new PaddleApiError(
      "invalid_checkout_url",
      "Paddle returned an insecure checkout URL."
    );
  }

  return parsed.toString();
}