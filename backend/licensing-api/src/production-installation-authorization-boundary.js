import {
  D1InstallationAuthorizationStore
} from "./installation-authorization-store.js";

import {
  createInstallationAuthorizationService
} from "./installation-authorization-service.js";

import {
  createProductionTokenIssuer
} from "./production-token-issuer.js";

const INTERNAL_HOST =
  "licensing-v2.internal";

const INTERNAL_ISSUE_PATH =
  "/internal/installation-authorization";

const PUBLIC_EXCHANGE_PATH =
  "/installation-authorization/exchange";

const INTERNAL_BODY_LIMIT =
  4096;

const PUBLIC_BODY_LIMIT =
  1024;

const DEFAULT_AUTHORIZATION_LIFETIME_SECONDS =
  300;

const DEFAULT_TOKEN_LIFETIME_SECONDS =
  300;

const JSON_HEADERS = Object.freeze({
  "content-type":
    "application/json; charset=utf-8",
  "cache-control":
    "no-store"
});

export function isProductionInternalInstallationAuthorizationRequest(
  request
) {
  const url =
    parseRequestUrl(request);

  if (url === null) {
    return false;
  }

  return (
    url.protocol === "https:" &&
    url.hostname === INTERNAL_HOST &&
    url.pathname === INTERNAL_ISSUE_PATH
  );
}

export function isProductionInstallationAuthorizationExchangeRequest(
  request
) {
  const url =
    parseRequestUrl(request);

  if (url === null) {
    return false;
  }

  return (
    url.protocol === "https:" &&
    url.hostname !== INTERNAL_HOST &&
    url.pathname === PUBLIC_EXCHANGE_PATH
  );
}

export function createProductionInstallationAuthorizationBoundary({
  database,
  identitySecret,
  authorizationClock,
  tokenClock,
  randomBytes,
  authorizationLifetimeSeconds =
    DEFAULT_AUTHORIZATION_LIFETIME_SECONDS,
  tokenLifetimeSeconds =
    DEFAULT_TOKEN_LIFETIME_SECONDS
} = {}) {
  const store =
    new D1InstallationAuthorizationStore(
      database
    );

  const authorizationService =
    createInstallationAuthorizationService({
      store,

      ...(authorizationClock === undefined
        ? {}
        : {
            clock:
              authorizationClock
          }),

      ...(randomBytes === undefined
        ? {}
        : {
            randomBytes
          }),

      lifetimeSeconds:
        authorizationLifetimeSeconds
    });

  const issueProductionToken =
    createProductionTokenIssuer({
      secret:
        identitySecret,

      ...(tokenClock === undefined
        ? {}
        : {
            clock:
              tokenClock
          }),

      lifetimeSeconds:
        tokenLifetimeSeconds
    });

  return Object.freeze({
    issueInternal:
      createInternalIssueHandler({
        authorizationService
      }),

    exchangePublic:
      createPublicExchangeHandler({
        authorizationService,
        issueProductionToken,
        tokenLifetimeSeconds
      })
  });
}

function createInternalIssueHandler({
  authorizationService
}) {
  return async function issueInternal(
    request
  ) {
    if (
      !isProductionInternalInstallationAuthorizationRequest(
        request
      )
    ) {
      return json(
        404,
        {
          error:
            "not_found"
        }
      );
    }

    const methodError =
      requirePost(request);

    if (methodError !== null) {
      return methodError;
    }

    const bodyResult =
      await readJsonObject(
        request,
        INTERNAL_BODY_LIMIT
      );

    if (!bodyResult.ok) {
      return json(
        400,
        {
          error:
            bodyResult.error
        }
      );
    }

    /*
     * TRUST BOUNDARY
     *
     * accountId/productId are accepted only on the exact
     * service-binding URL. This handler must never be
     * exposed through the public Worker host.
     */
    let accountId;
    let productId;

    try {
      accountId =
        requireClaim(
          bodyResult.value.accountId,
          "accountId"
        );

      productId =
        requireClaim(
          bodyResult.value.productId,
          "productId"
        );
    } catch {
      return json(
        400,
        {
          error:
            "invalid_identity"
        }
      );
    }

    let result;

    try {
      result =
        await authorizationService.issue({
          accountId,
          productId
        });
    } catch {
      return json(
        503,
        {
          error:
            "authorization_unavailable"
        }
      );
    }

    if (
      !result ||
      typeof result.code !== "string" ||
      !/^[A-Za-z0-9_-]{43}$/.test(
        result.code
      ) ||
      !Number.isInteger(
        result.expiresInSeconds
      )
    ) {
      return json(
        503,
        {
          error:
            "authorization_unavailable"
        }
      );
    }

    return json(
      200,
      {
        authorizationCode:
          result.code,
        expiresInSeconds:
          result.expiresInSeconds
      }
    );
  };
}

function createPublicExchangeHandler({
  authorizationService,
  issueProductionToken,
  tokenLifetimeSeconds
}) {
  return async function exchangePublic(
    request
  ) {
    if (
      !isProductionInstallationAuthorizationExchangeRequest(
        request
      )
    ) {
      return json(
        404,
        {
          error:
            "not_found"
        }
      );
    }

    const methodError =
      requirePost(request);

    if (methodError !== null) {
      return methodError;
    }

    const bodyResult =
      await readJsonObject(
        request,
        PUBLIC_BODY_LIMIT
      );

    if (!bodyResult.ok) {
      return json(
        400,
        {
          error:
            bodyResult.error
        }
      );
    }

    const authorizationCode =
      bodyResult.value.authorizationCode;

    if (
      typeof authorizationCode !==
      "string"
    ) {
      return json(
        400,
        {
          error:
            "invalid_request"
        }
      );
    }

    /*
     * Public request identity is deliberately ignored.
     *
     * accountId/productId used below may only come from
     * the atomically consumed server-side authorization
     * record.
     */
    let exchangeResult;

    try {
      exchangeResult =
        await authorizationService.exchange(
          authorizationCode
        );
    } catch {
      return json(
        401,
        {
          error:
            "invalid_authorization"
        }
      );
    }

    if (
      !exchangeResult ||
      exchangeResult.authorized !== true
    ) {
      return json(
        401,
        {
          error:
            "invalid_authorization"
        }
      );
    }

    let accountId;
    let productId;

    try {
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
        {
          error:
            "invalid_authorization"
        }
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
        {
          error:
            "token_unavailable"
        }
      );
    }

    if (
      typeof token !== "string" ||
      token.length === 0
    ) {
      return json(
        503,
        {
          error:
            "token_unavailable"
        }
      );
    }

    return json(
      200,
      {
        token,
        tokenType:
          "Bearer",
        expiresInSeconds:
          tokenLifetimeSeconds
      }
    );
  };
}

function requirePost(request) {
  if (!(request instanceof Request)) {
    return json(
      400,
      {
        error:
          "invalid_request"
      }
    );
  }

  if (request.method !== "POST") {
    return new Response(
      JSON.stringify({
        error:
          "method_not_allowed"
      }),
      {
        status:
          405,
        headers: {
          ...JSON_HEADERS,
          allow:
            "POST"
        }
      }
    );
  }

  return null;
}

async function readJsonObject(
  request,
  maximumBytes
) {
  const contentType =
    request.headers.get(
      "content-type"
    ) ?? "";

  if (
    !contentType
      .toLowerCase()
      .startsWith(
        "application/json"
      )
  ) {
    return {
      ok:
        false,
      error:
        "invalid_request"
    };
  }

  const declaredLength =
    request.headers.get(
      "content-length"
    );

  if (declaredLength !== null) {
    const parsedLength =
      Number(declaredLength);

    if (
      !Number.isSafeInteger(
        parsedLength
      ) ||
      parsedLength < 0 ||
      parsedLength > maximumBytes
    ) {
      return {
        ok:
          false,
        error:
          "invalid_request"
      };
    }
  }

  let text;

  try {
    text =
      await request.text();
  } catch {
    return {
      ok:
        false,
      error:
        "invalid_request"
    };
  }

  if (
    text.length === 0 ||
    new TextEncoder()
      .encode(text)
      .byteLength > maximumBytes
  ) {
    return {
      ok:
        false,
      error:
        "invalid_request"
    };
  }

  let value;

  try {
    value =
      JSON.parse(text);
  } catch {
    return {
      ok:
        false,
      error:
        "invalid_json"
    };
  }

  if (
    !value ||
    typeof value !== "object" ||
    Array.isArray(value)
  ) {
    return {
      ok:
        false,
      error:
        "invalid_request"
    };
  }

  return {
    ok:
      true,
    value
  };
}

function requireClaim(
  value,
  name
) {
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

function parseRequestUrl(request) {
  if (
    !request ||
    typeof request.url !== "string"
  ) {
    return null;
  }

  try {
    return new URL(
      request.url
    );
  } catch {
    return null;
  }
}

function json(
  status,
  body
) {
  return new Response(
    JSON.stringify(body),
    {
      status,
      headers:
        JSON_HEADERS
    }
  );
}