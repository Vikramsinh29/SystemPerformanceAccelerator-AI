const CUSTOMER_ID =
  /^ctm_[a-z\d]{26}$/;

const ADDRESS_ID =
  /^add_[a-z\d]{26}$/;

export function createPaddleSandboxBillingProfileClient({
  apiKey,
  fetchImpl
} = {}) {
  if (
    typeof apiKey !== "string" ||
    !apiKey.startsWith("pdl_sdbx_apikey_")
  ) {
    throw new TypeError(
      "Valid Paddle sandbox API key is required."
    );
  }

  if (typeof fetchImpl !== "function") {
    throw new TypeError("fetchImpl is required.");
  }

  return Object.freeze({
    async createBillingProfile({
      email,
      name,
      countryCode,
      postalCode
    } = {}) {
      requireText(email, "email");
      requireText(name, "name");

      if (!/^[A-Z]{2}$/.test(countryCode ?? "")) {
        throw new TypeError(
          "countryCode must be ISO-3166 alpha-2."
        );
      }

      requireText(
        postalCode,
        "postalCode"
      );

      const headers = {
        Authorization:
          `Bearer ${apiKey}`,

        "Content-Type":
          "application/json",

        "Paddle-Version":
          "1"
      };

      const customerResponse =
        await fetchImpl(
          "https://sandbox-api.paddle.com/customers",
          {
            method: "POST",
            headers,
            body: JSON.stringify({
              email,
              name
            })
          }
        );

      const customer =
        await readJson(
          customerResponse,
          "customer"
        );

      const customerId =
        customer?.data?.id;

      if (
        typeof customerId !== "string" ||
        !CUSTOMER_ID.test(customerId)
      ) {
        throw new Error(
          "Paddle returned an invalid customer."
        );
      }

      const addressResponse =
        await fetchImpl(
          `https://sandbox-api.paddle.com/customers/${customerId}/addresses`,
          {
            method: "POST",
            headers,
            body: JSON.stringify({
              country_code:
                countryCode,

              postal_code:
                postalCode
            })
          }
        );

      const address =
        await readJson(
          addressResponse,
          "address"
        );

      const addressId =
        address?.data?.id;

      if (
        typeof addressId !== "string" ||
        !ADDRESS_ID.test(addressId)
      ) {
        throw new Error(
          "Paddle returned an invalid address."
        );
      }

      return Object.freeze({
        customerId,
        addressId
      });
    }
  });
}

async function readJson(
  response,
  resource
) {
  if (
    !response ||
    typeof response.status !== "number" ||
    typeof response.text !== "function"
  ) {
    throw new Error(
      `Invalid Paddle ${resource} response.`
    );
  }

  const raw =
    await response.text();

  let payload = null;

  if (raw.length > 0) {
    try {
      payload = JSON.parse(raw);
    } catch {
      throw new Error(
        `Invalid Paddle ${resource} response.`
      );
    }
  }

  if (
    response.status < 200 ||
    response.status >= 300
  ) {
    throw new Error(
      `Paddle ${resource} request failed.`
    );
  }

  return payload;
}

function requireText(
  value,
  field
) {
  if (
    typeof value !== "string" ||
    value.trim().length === 0 ||
    value.length > 200
  ) {
    throw new TypeError(
      `${field} is required.`
    );
  }
}
