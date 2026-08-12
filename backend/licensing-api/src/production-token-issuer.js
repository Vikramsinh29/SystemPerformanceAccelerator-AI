const TOKEN_PREFIX = "pcspa1";
const TOKEN_AUDIENCE = "pc-spa-licensing-v2";
const encoder = new TextEncoder();

export function createProductionTokenIssuer({ secret, clock = () => Date.now(), lifetimeSeconds = 300 } = {}) {
  if (typeof secret !== "string" || secret.length < 32) {
    throw new TypeError("A production identity secret of at least 32 characters is required.");
  }
  if (typeof clock !== "function") {
    throw new TypeError("clock must be a function.");
  }
  if (!Number.isInteger(lifetimeSeconds) || lifetimeSeconds < 30 || lifetimeSeconds > 900) {
    throw new TypeError("lifetimeSeconds must be an integer between 30 and 900.");
  }

  return async function issueProductionIdentityToken({ accountId, productId } = {}) {
    const nowSeconds = Math.floor(Number(clock()) / 1000);
    if (!Number.isFinite(nowSeconds)) {
      throw new TypeError("clock must return a finite millisecond timestamp.");
    }

    const sub = requireClaim(accountId, "accountId");
    const product = productId === undefined ? undefined : requireClaim(productId, "productId");

    const payload = {
      v: 1,
      aud: TOKEN_AUDIENCE,
      sub,
      exp: nowSeconds + lifetimeSeconds
    };

    if (product !== undefined) payload.product = product;

    const payloadPart = base64Url(encoder.encode(JSON.stringify(payload)));
    const signingInput = `${TOKEN_PREFIX}.${payloadPart}`;
    const signature = await hmac(secret, signingInput);

    return `${signingInput}.${base64Url(signature)}`;
  };
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

function base64Url(bytes) {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary)
    .replaceAll("+", "-")
    .replaceAll("/", "_")
    .replace(/=+$/g, "");
}

function requireClaim(value, name) {
  if (typeof value !== "string" || value.trim().length === 0 || value.length > 128) {
    throw new TypeError(`${name} must be a non-empty string no longer than 128 characters.`);
  }
  return value.trim();
}
