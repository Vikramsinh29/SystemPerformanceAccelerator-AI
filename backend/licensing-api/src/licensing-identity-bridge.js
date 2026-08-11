export class LicensingIdentityError extends Error {
  constructor(code) {
    super(code);
    this.name = "LicensingIdentityError";
    this.code = code;
  }
}

export function createLicensingIdentityBridge({
  resolveAuthenticatedAccount,
  productId = "pcspa-pro"
}) {
  if (typeof resolveAuthenticatedAccount !== "function") {
    throw new TypeError("resolveAuthenticatedAccount must be a function.");
  }
  requireText(productId, "productId");

  return {
    async resolve(request) {
      const account = await resolveAuthenticatedAccount(request);
      if (!account) throw new LicensingIdentityError("unauthenticated");
      if (typeof account !== "object") {
        throw new TypeError("authenticated account must be an object.");
      }

      const accountId = requireText(account.accountId, "account.accountId");
      const resolvedProductId = account.productId == null
        ? productId
        : requireText(account.productId, "account.productId");

      return Object.freeze({ accountId, productId: resolvedProductId });
    }
  };
}

function requireText(value, name) {
  if (typeof value !== "string" || value.trim().length === 0) {
    throw new TypeError(`${name} is required.`);
  }
  return value.trim();
}
