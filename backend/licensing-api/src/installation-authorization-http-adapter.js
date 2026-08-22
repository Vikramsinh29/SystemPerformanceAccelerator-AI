const JSON_HEADERS = Object.freeze({
  "content-type": "application/json; charset=utf-8",
  "cache-control": "no-store"
});

const MAX_EXCHANGE_BODY_BYTES = 1024;
const DEFAULT_TOKEN_LIFETIME_SECONDS = 300;

export function createInstallationAuthorizationHttpAdapter({
  resolveAuthenticatedAccount,
  authorizationService,
  issueProductionToken,
  tokenLifetimeSeconds = DEFAULT_TOKEN_LIFETIME_SECONDS
} = {}) {
  if (typeof resolveAuthenticatedAccount !== "function") {
    throw new TypeError(
      "resolveAuthenticatedAccount must be a function."
    );
  }

  if (
    !authorizationService ||
    typeof authorizationService.issue !== "function" ||
    typeof authorizationService.exchange !== "function"
  ) {
    throw new TypeError(
      "authorizationService must provide issue and exchange."
    );
  }

  if (typeof issueProductionToken !== "function") {
    throw new TypeError(
      "issueProductionToken must be a function."
    );
  }

  if (
    !Number.isInteger(tokenLifetimeSeconds) ||
    tokenLifetimeSeconds < 30 ||
    tokenLifetimeSeconds > 900
  ) {
    throw new TypeError(
      "tokenLifetimeSeconds must be between 30 and 900."
    );
  }

  return Object.freeze({
    issue: createIssueHandler({
      resolveAuthenticatedAccount,
      authorizationService
    }),

    exchange: createExchangeHandler({
      authorizationService,
      issueProductionToken,
      tokenLifetimeSeconds
    })
  });
}

function createIssueHandler({
  resolveAuthenticatedAccount,
  authorizationService
}) {
  return async function issueInstallationAuthorization(
    request
  ) {
    const methodError =
      validatePostRequest(request);

    if (methodError) {
      return methodError;
    }

    /*
     * SECURITY BOUNDARY
     *
     * No accountId or productId is read from the request body.
     * Identity comes exclusively from the trusted authenticated
     * session resolver.
     */
    let trustedIdentity;

    try {
      trustedIdentity =
        await resolveAuthenticatedAccount(request);
    } catch {
      return json(
        503,
        { error: "identity_unavailable" }
      );
    }

    if (trustedIdentity == null) {
      return json(
        401,
        { error: "unauthenticated" }
      );
    }

    if (
      typeof trustedIdentity !== "object" ||
      Array.isArray(trustedIdentity)
    ) {
      return json(
        503,
        { error: "identity_unavailable" }
      );
    }

    let accountId;
    let productId;

    try {
      accountId =
        requireClaim(
          trustedIdentity.accountId,
          "trustedIdentity.accountId"
        );

      productId =
        requireClaim(
          trustedIdentity.productId,
          "trustedIdentity.productId"
        );
    } catch {
      return json(
        503,
        { error: "identity_unavailable" }
      );
    }

    try {
      const result =
        await authorizationService.issue({
          accountId,
          productId
        });

      if (
        !result ||
        typeof result.code !== "string" ||
        !/^[A-Za-z0-9_-]{43}$/.test(result.code) ||
        !Number.isInteger(result.expiresInSeconds)
      ) {
        return json(
          503,
          { error: "authorization_unavailable" }
        );
      }

      return json(
        200,
        {
          authorizationCode: result.code,
          expiresInSeconds:
            result.expiresInSeconds
        }
      );
    } catch {
      return json(
        503,
        { error: "authorization_unavailable" }
      );
    }
  };
}

function createExchangeHandler({
  authorizationService,
  issueProductionToken,
  tokenLifetimeSeconds
}) {
  return async function exchangeInstallationAuthorization(
    request
  ) {
    const methodError =
      validatePostRequest(request);

    if (methodError) {
      return methodError;
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
      const parsed =
        Number(contentLength);

      if (
        !Number.isSafeInteger(parsed) ||
        parsed < 0 ||
        parsed > MAX_EXCHANGE_BODY_BYTES
      ) {
        return json(
          400,
          { error: "invalid_request" }
        );
      }
    }

    let rawBody;

    try {
      rawBody =
        await request.text();
    } catch {
      return json(
        400,
        { error: "invalid_request" }
      );
    }

    if (
      rawBody.length === 0 ||
      new TextEncoder()
        .encode(rawBody)
        .byteLength > MAX_EXCHANGE_BODY_BYTES
    ) {
      return json(
        400,
        { error: "invalid_request" }
      );
    }

    let body;

    try {
      body =
        JSON.parse(rawBody);
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
     * The desktop is permitted to submit only the opaque
     * one-time authorization code.
     *
     * Any accountId/productId values in the JSON body are
     * deliberately ignored and never forwarded.
     */
    if (
      typeof body.authorizationCode !== "string"
    ) {
      return json(
        400,
        { error: "invalid_request" }
      );
    }

    let exchangeResult;

    try {
      exchangeResult =
        await authorizationService.exchange(
          body.authorizationCode
        );
    } catch {
      return json(
        401,
        { error: "invalid_authorization" }
      );
    }

    if (
      !exchangeResult ||
      exchangeResult.authorized !== true
    ) {
      return json(
        401,
        { error: "invalid_authorization" }
      );
    }

    let accountId;
    let productId;

    try {
      /*
       * These values came from the atomically consumed
       * server-side authorization record, not the desktop.
       */
      accountId =
        requireClaim(
          exchangeResult.accountId,
          "exchangeResult.accountId"
        );

      productId =
        requireClaim(
          exchangeResult.productId,
          "exchangeResult.productId"
        );
    } catch {
      return json(
        401,
        { error: "invalid_authorization" }
      );
    }

    let token;

    try {
      token =
        await issueProductionToken({
          accountId,
          productId
        });
    } catch {
      return json(
        503,
        { error: "token_unavailable" }
      );
    }

    if (
      typeof token !== "string" ||
      token.length === 0
    ) {
      return json(
        503,
        { error: "token_unavailable" }
      );
    }

    return json(
      200,
      {
        token,
        tokenType: "Bearer",
        expiresInSeconds:
          tokenLifetimeSeconds
      }
    );
  };
}

function validatePostRequest(request) {
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

  return null;
}

function requireClaim(value, name) {
  if (
    typeof value !== "string" ||
    value.trim().length === 0 ||
    value.trim().length > 128
  ) {
    throw new TypeError(
      `${name} must be a non-empty string no longer than 128 characters.`
    );
  }

  return value.trim();
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