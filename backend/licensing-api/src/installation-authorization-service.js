const DEFAULT_LIFETIME_SECONDS = 300;
const CODE_BYTE_LENGTH = 32;

export function createInstallationAuthorizationService({
  store,
  clock = () => new Date(),
  randomBytes = defaultRandomBytes,
  lifetimeSeconds = DEFAULT_LIFETIME_SECONDS
} = {}) {
  if (
    !store ||
    typeof store.createAuthorization !== "function" ||
    typeof store.consumeAuthorization !== "function"
  ) {
    throw new TypeError(
      "store must provide createAuthorization and consumeAuthorization."
    );
  }

  if (typeof clock !== "function") {
    throw new TypeError("clock must be a function.");
  }

  if (typeof randomBytes !== "function") {
    throw new TypeError("randomBytes must be a function.");
  }

  if (
    !Number.isInteger(lifetimeSeconds) ||
    lifetimeSeconds < 60 ||
    lifetimeSeconds > 600
  ) {
    throw new TypeError(
      "lifetimeSeconds must be an integer between 60 and 600."
    );
  }

  return Object.freeze({
    async issue({ accountId, productId } = {}) {
      const trustedAccountId =
        requireClaim(accountId, "accountId");

      const trustedProductId =
        requireClaim(productId, "productId");

      const created =
        requireDate(clock(), "clock");

      const entropy =
        normalizeEntropy(
          await randomBytes(CODE_BYTE_LENGTH)
        );

      const code =
        encodeBase64Url(entropy);

      const codeSha256 =
        await sha256Hex(code);

      const authorizationId =
        `ia_${codeSha256.slice(0, 32)}`;

      const createdUtc =
        created.toISOString();

      const expiresUtc =
        new Date(
          created.getTime() + lifetimeSeconds * 1000
        ).toISOString();

      await store.createAuthorization({
        authorizationId,
        codeSha256,
        accountId: trustedAccountId,
        productId: trustedProductId,
        createdUtc,
        expiresUtc
      });

      return Object.freeze({
        code,
        expiresInSeconds: lifetimeSeconds
      });
    },

    async exchange(code) {
      if (
        typeof code !== "string" ||
        !/^[A-Za-z0-9_-]{43}$/.test(code)
      ) {
        return Object.freeze({
          authorized: false
        });
      }

      const now =
        requireDate(clock(), "clock");

      const codeSha256 =
        await sha256Hex(code);

      let consumed;

      try {
        consumed =
          await store.consumeAuthorization(
            codeSha256,
            now.toISOString()
          );
      } catch {
        return Object.freeze({
          authorized: false
        });
      }

      if (!consumed) {
        return Object.freeze({
          authorized: false
        });
      }

      try {
        return Object.freeze({
          authorized: true,
          accountId:
            requireClaim(
              consumed.account_id,
              "consumed.account_id"
            ),
          productId:
            requireClaim(
              consumed.product_id,
              "consumed.product_id"
            )
        });
      } catch {
        return Object.freeze({
          authorized: false
        });
      }
    }
  });
}

async function defaultRandomBytes(length) {
  const bytes =
    new Uint8Array(length);

  globalThis.crypto.getRandomValues(bytes);

  return bytes;
}

function normalizeEntropy(value) {
  if (!(value instanceof Uint8Array)) {
    throw new TypeError(
      "randomBytes must return a Uint8Array."
    );
  }

  if (value.length !== CODE_BYTE_LENGTH) {
    throw new TypeError(
      `randomBytes must return exactly ${CODE_BYTE_LENGTH} bytes.`
    );
  }

  return value;
}

function requireDate(value, name) {
  const date =
    value instanceof Date
      ? value
      : new Date(value);

  if (Number.isNaN(date.getTime())) {
    throw new TypeError(`${name} must return a valid date.`);
  }

  return date;
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

async function sha256Hex(value) {
  const bytes =
    new TextEncoder().encode(value);

  const digest =
    await globalThis.crypto.subtle.digest(
      "SHA-256",
      bytes
    );

  return Array.from(
    new Uint8Array(digest),
    byte => byte.toString(16).padStart(2, "0")
  ).join("");
}

function encodeBase64Url(bytes) {
  let binary = "";

  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }

  return btoa(binary)
    .replace(/\+/g, "-")
    .replace(/\//g, "_")
    .replace(/=+$/g, "");
}