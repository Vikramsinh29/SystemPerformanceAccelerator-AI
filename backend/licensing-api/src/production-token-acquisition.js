import { createProductionTokenIssuer } from "./production-token-issuer.js";

const JSON_HEADERS = Object.freeze({
  "content-type": "application/json; charset=utf-8",
  "cache-control": "no-store"
});

const DEFAULT_PRODUCT_ID = "pcspa-pro";
const DEFAULT_LIFETIME_SECONDS = 300;

export function createProductionTokenAcquisitionHandler({
  resolveAuthenticatedAccount,
  identitySecret,
  productId = DEFAULT_PRODUCT_ID,
  clock,
  lifetimeSeconds = DEFAULT_LIFETIME_SECONDS
} = {}) {
  if (typeof resolveAuthenticatedAccount !== "function") {
    throw new TypeError("resolveAuthenticatedAccount must be a function.");
  }

  const trustedProductId = requireClaim(productId, "productId");
  const issueToken = createProductionTokenIssuer({
    secret: identitySecret,
    ...(clock === undefined ? {} : { clock }),
    lifetimeSeconds
  });

  return Object.freeze({
    async fetch(request) {
      if (!request || typeof request.method !== "string") {
        return json(400, { error: "invalid_request" });
      }

      if (request.method.toUpperCase() !== "POST") {
        return new Response(JSON.stringify({ error: "method_not_allowed" }), {
          status: 405,
          headers: { ...JSON_HEADERS, allow: "POST" }
        });
      }

      let account;
      try {
        account = await resolveAuthenticatedAccount(request);
      } catch {
        return json(503, { error: "identity_unavailable" });
      }

      if (!account) {
        return json(401, { error: "unauthenticated" });
      }

      if (typeof account !== "object" || Array.isArray(account)) {
        return json(503, { error: "identity_unavailable" });
      }

      try {
        const accountId = requireClaim(account.accountId, "account.accountId");
        const resolvedProductId = account.productId == null
          ? trustedProductId
          : requireClaim(account.productId, "account.productId");

        const token = await issueToken({
          accountId,
          productId: resolvedProductId
        });

        return json(200, {
          token,
          tokenType: "Bearer",
          expiresInSeconds: lifetimeSeconds
        });
      } catch {
        return json(503, { error: "identity_unavailable" });
      }
    }
  });
}

function requireClaim(value, name) {
  if (typeof value !== "string" || value.trim().length === 0 || value.length > 128) {
    throw new TypeError(`${name} must be a non-empty string no longer than 128 characters.`);
  }
  return value.trim();
}

function json(status, body) {
  return new Response(JSON.stringify(body), {
    status,
    headers: JSON_HEADERS
  });
}
