const JSON_HEADERS = Object.freeze({
  "content-type": "application/json; charset=utf-8"
});

const MAX_BODY_BYTES = 4096;

export function createCommercialCheckoutHttpAdapter({
  checkoutOrchestrator
} = {}) {
  if (
    !checkoutOrchestrator ||
    typeof checkoutOrchestrator.createCheckout !== "function"
  ) {
    throw new TypeError(
      "checkoutOrchestrator.createCheckout is required."
    );
  }

  return async function handleCommercialCheckout(
    request,
    trustedIdentity
  ) {
    if (!(request instanceof Request)) {
      return json(
        400,
        { error: "invalid_request" }
      );
    }

    if (request.method !== "POST") {
      return new Response(
        JSON.stringify({
          error: "method_not_allowed"
        }),
        {
          status: 405,
          headers: {
            ...JSON_HEADERS,
            allow: "POST"
          }
        }
      );
    }

    if (
      !trustedIdentity ||
      trustedIdentity.authenticated !== true ||
      typeof trustedIdentity.accountId !== "string" ||
      trustedIdentity.accountId.trim().length === 0
    ) {
      return json(
        401,
        { error: "unauthenticated" }
      );
    }

    const contentType =
      request.headers.get("content-type") ?? "";

    if (
      !contentType
        .toLowerCase()
        .startsWith("application/json")
    ) {
      return json(
        400,
        { error: "invalid_request" }
      );
    }

    const contentLength =
      request.headers.get("content-length");

    if (contentLength !== null) {
      const parsedLength = Number(contentLength);

      if (
        !Number.isSafeInteger(parsedLength) ||
        parsedLength < 0 ||
        parsedLength > MAX_BODY_BYTES
      ) {
        return json(
          400,
          { error: "invalid_request" }
        );
      }
    }

    let rawBody;

    try {
      rawBody = await request.text();
    } catch {
      return json(
        400,
        { error: "invalid_request" }
      );
    }

    if (
      rawBody.length === 0 ||
      new TextEncoder().encode(rawBody).byteLength >
        MAX_BODY_BYTES
    ) {
      return json(
        400,
        { error: "invalid_request" }
      );
    }

    let body;

    try {
      body = JSON.parse(rawBody);
    } catch {
      return json(
        400,
        { error: "invalid_json" }
      );
    }

    if (
      !body ||
      typeof body !== "object" ||
      Array.isArray(body)
    ) {
      return json(
        400,
        { error: "invalid_request" }
      );
    }

    /*
     * SECURITY BOUNDARY
     *
     * Only these client-controlled commercial choices
     * cross the HTTP boundary.
     *
     * accountId, priceId, provider IDs, currency,
     * amounts and entitlement state are deliberately
     * NOT forwarded from the request.
     */
    const command = {
      accountId: trustedIdentity.accountId,

      /*
       * Public checkout terminology is billingInterval,
       * while the commercial catalog resolves the approved
       * monthly/annual plan internally.
       */
      plan: body.billingInterval,

      seats: body.seats
    };

    if (
      typeof trustedIdentity.productId === "string" &&
      trustedIdentity.productId.trim().length > 0
    ) {
      command.productId =
        trustedIdentity.productId;
    }

    try {
      const result =
        await checkoutOrchestrator.createCheckout(
          command
        );

      if (
        !result ||
        typeof result !== "object" ||
        typeof result.checkoutUrl !== "string"
      ) {
        return json(
          502,
          { error: "checkout_unavailable" }
        );
      }

      let checkoutUrl;

      try {
        checkoutUrl =
          new URL(result.checkoutUrl);
      } catch {
        return json(
          502,
          { error: "checkout_unavailable" }
        );
      }

      if (checkoutUrl.protocol !== "https:") {
        return json(
          502,
          { error: "checkout_unavailable" }
        );
      }

      return json(
        200,
        {
          checkoutUrl:
            checkoutUrl.toString()
        }
      );
    } catch (error) {
      return mapCheckoutError(error);
    }
  };
}

function mapCheckoutError(error) {
  const code =
    typeof error?.code === "string"
      ? error.code
      : "";

  if (
    code === "invalid_plan" ||
    code === "unsupported_plan" ||
    code === "invalid_billing_interval" ||
    code === "invalid_seat_count" ||
    code === "invalid_seat_quantity" ||
    code === "invalid_checkout_request"
  ) {
    return json(
      400,
      { error: "invalid_checkout_request" }
    );
  }

  if (
    code === "plan_not_found" ||
    code === "price_not_found" ||
    code === "price_unavailable"
  ) {
    return json(
      409,
      { error: "checkout_unavailable" }
    );
  }

  /*
   * Provider and unexpected implementation details
   * must never cross the public HTTP boundary.
   */
  return json(
    502,
    { error: "checkout_unavailable" }
  );
}

function json(status, body) {
  return new Response(
    JSON.stringify(body),
    {
      status,
      headers: JSON_HEADERS
    }
  );
}
