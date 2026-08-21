import {
  LicensingIdentityError
} from "./licensing-identity-bridge.js";

export function createCommercialCheckoutComposition({
  identityBridge,
  checkoutHttpAdapter
} = {}) {
  if (
    !identityBridge ||
    typeof identityBridge.resolve !== "function"
  ) {
    throw new TypeError(
      "identityBridge.resolve is required."
    );
  }

  if (
    typeof checkoutHttpAdapter !== "function"
  ) {
    throw new TypeError(
      "checkoutHttpAdapter is required."
    );
  }

  return async function handleCommercialCheckout(
    request
  ) {
    let identity;

    try {
      const resolved =
        await identityBridge.resolve(
          request
        );

      identity = Object.freeze({
        authenticated: true,
        accountId:
          resolved.accountId,
        productId:
          resolved.productId
      });
    } catch (error) {
      if (
        error instanceof LicensingIdentityError &&
        error.code === "unauthenticated"
      ) {
        return checkoutHttpAdapter(
          request,
          null
        );
      }

      /*
       * Identity-provider failures must fail closed
       * and must not be transformed into authenticated
       * checkout attempts.
       */
      return json(
        503,
        {
          error:
            "identity_unavailable"
        }
      );
    }

    return checkoutHttpAdapter(
      request,
      identity
    );
  };
}

function json(status, body) {
  return new Response(
    JSON.stringify(body),
    {
      status,
      headers: {
        "content-type":
          "application/json; charset=utf-8"
      }
    }
  );
}