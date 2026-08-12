const DEFAULT_PRODUCT_ID = "pcspa-pro";

export function createProductionAuthenticatedAccountResolver({
  verifySession,
  productId = DEFAULT_PRODUCT_ID
} = {}) {
  if (typeof verifySession !== "function") {
    throw new TypeError("verifySession must be a function.");
  }

  const trustedProductId = requireClaim(productId, "productId");

  return async function resolveAuthenticatedAccount(request) {
    if (!(request instanceof Request)) {
      throw new TypeError("request must be a Request.");
    }

    const session = await verifySession(request);

    if (session == null) {
      return null;
    }

    if (typeof session !== "object" || Array.isArray(session)) {
      throw new TypeError("verified session must be an object.");
    }

    const accountId = requireClaim(session.accountId, "session.accountId");
    const resolvedProductId = session.productId == null
      ? trustedProductId
      : requireClaim(session.productId, "session.productId");

    return Object.freeze({
      accountId,
      productId: resolvedProductId
    });
  };
}

function requireClaim(value, name) {
  if (typeof value !== "string" || value.trim().length === 0 || value.length > 128) {
    throw new TypeError(`${name} must be a non-empty string no longer than 128 characters.`);
  }

  return value.trim();
}
