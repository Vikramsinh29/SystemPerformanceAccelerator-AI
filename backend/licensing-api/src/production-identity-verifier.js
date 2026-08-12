const TOKEN_PREFIX = "pcspa1";
const TOKEN_AUDIENCE = "pc-spa-licensing-v2";
const encoder = new TextEncoder();

export class ProductionIdentityError extends Error {
  constructor(code = "unauthorized") {
    super(code);
    this.name = "ProductionIdentityError";
    this.code = code;
  }
}

export function createProductionIdentityResolver({ secret, clock = () => Date.now() } = {}) {
  if (typeof secret !== "string" || secret.length < 32) {
    throw new TypeError("A production identity secret of at least 32 characters is required.");
  }
  if (typeof clock !== "function") {
    throw new TypeError("clock must be a function.");
  }

  return async function resolveAuthenticatedAccount(request) {
    const token = readBearerToken(request);
    const payload = await verifyToken(token, secret);
    const nowSeconds = Math.floor(Number(clock()) / 1000);

    if (!Number.isFinite(nowSeconds)) {
      throw new TypeError("clock must return a finite millisecond timestamp.");
    }
    if (payload.v !== 1 || payload.aud !== TOKEN_AUDIENCE) {
      throw new ProductionIdentityError("invalid_identity_token");
    }
    if (!Number.isInteger(payload.exp) || payload.exp <= nowSeconds) {
      throw new ProductionIdentityError("expired_identity_token");
    }

    const accountId = requireClaim(payload.sub, "sub");
    const productId = payload.product === undefined
      ? undefined
      : requireClaim(payload.product, "product");

    return productId === undefined ? { accountId } : { accountId, productId };
  };
}

function readBearerToken(request) {
  const authorization = request?.headers?.get?.("authorization");
  if (typeof authorization !== "string") {
    throw new ProductionIdentityError("missing_identity_token");
  }
  const match = /^Bearer\s+([^\s]+)$/i.exec(authorization.trim());
  if (!match) {
    throw new ProductionIdentityError("invalid_identity_token");
  }
  return match[1];
}

async function verifyToken(token, secret) {
  const parts = String(token).split(".");
  if (parts.length !== 3 || parts[0] !== TOKEN_PREFIX) {
    throw new ProductionIdentityError("invalid_identity_token");
  }

  const signingInput = `${parts[0]}.${parts[1]}`;
  const expected = await hmac(secret, signingInput);
  const provided = decodeBase64Url(parts[2]);

  if (!constantTimeEqual(expected, provided)) {
    throw new ProductionIdentityError("invalid_identity_token");
  }

  try {
    const json = new TextDecoder().decode(decodeBase64Url(parts[1]));
    const payload = JSON.parse(json);
    if (!payload || typeof payload !== "object" || Array.isArray(payload)) throw new Error();
    return payload;
  } catch {
    throw new ProductionIdentityError("invalid_identity_token");
  }
}

async function hmac(secret, value) {
  const key = await crypto.subtle.importKey(
    "raw",
    encoder.encode(secret),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"]
  );
  return new Uint8Array(await crypto.subtle.sign("HMAC", key, encoder.encode(value)));
}

function decodeBase64Url(value) {
  if (typeof value !== "string" || !/^[A-Za-z0-9_-]+$/.test(value)) {
    throw new ProductionIdentityError("invalid_identity_token");
  }
  const padded = value.replace(/-/g, "+").replace(/_/g, "/") + "===".slice((value.length + 3) % 4);
  try {
    return Uint8Array.from(atob(padded), character => character.charCodeAt(0));
  } catch {
    throw new ProductionIdentityError("invalid_identity_token");
  }
}

function constantTimeEqual(left, right) {
  if (!(left instanceof Uint8Array) || !(right instanceof Uint8Array) || left.length !== right.length) {
    return false;
  }
  let difference = 0;
  for (let index = 0; index < left.length; index += 1) {
    difference |= left[index] ^ right[index];
  }
  return difference === 0;
}

function requireClaim(value, name) {
  if (typeof value !== "string" || value.trim().length === 0 || value.length > 128) {
    throw new ProductionIdentityError(`invalid_${name}_claim`);
  }
  return value.trim();
}
