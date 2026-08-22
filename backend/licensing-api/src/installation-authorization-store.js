export class D1InstallationAuthorizationStore {
  constructor(database) {
    if (!database?.prepare) {
      throw new TypeError("A D1-compatible database binding is required.");
    }

    this.database = database;
  }

  async createAuthorization(record) {
    validateRecord(record);

    const result = await this.database.prepare(`
      INSERT INTO installation_authorizations (
        authorization_id,
        code_sha256,
        account_id,
        product_id,
        created_utc,
        expires_utc,
        consumed_utc
      )
      VALUES (?, ?, ?, ?, ?, ?, NULL)
    `).bind(
      record.authorizationId,
      record.codeSha256,
      record.accountId,
      record.productId,
      record.createdUtc,
      record.expiresUtc
    ).run();

    if (changes(result) !== 1) {
      throw new Error("Installation authorization was not persisted.");
    }
  }

  async consumeAuthorization(codeSha256, nowUtc) {
    requireHash(codeSha256, "codeSha256");
    requireIso(nowUtc, "nowUtc");

    return this.database.prepare(`
      UPDATE installation_authorizations
         SET consumed_utc = ?
       WHERE code_sha256 = ?
         AND consumed_utc IS NULL
         AND expires_utc > ?
      RETURNING
        authorization_id,
        account_id,
        product_id,
        created_utc,
        expires_utc,
        consumed_utc
    `).bind(
      nowUtc,
      codeSha256,
      nowUtc
    ).first();
  }
}

function validateRecord(record) {
  if (!record || typeof record !== "object" || Array.isArray(record)) {
    throw new TypeError("record is required.");
  }

  requireText(record.authorizationId, "record.authorizationId");
  requireHash(record.codeSha256, "record.codeSha256");
  requireClaim(record.accountId, "record.accountId");
  requireClaim(record.productId, "record.productId");
  requireIso(record.createdUtc, "record.createdUtc");
  requireIso(record.expiresUtc, "record.expiresUtc");

  if (Date.parse(record.expiresUtc) <= Date.parse(record.createdUtc)) {
    throw new TypeError("record.expiresUtc must be later than record.createdUtc.");
  }
}

function requireClaim(value, name) {
  requireText(value, name);

  if (value.trim().length > 128) {
    throw new TypeError(`${name} must not exceed 128 characters.`);
  }
}

function requireHash(value, name) {
  requireText(value, name);

  if (!/^[a-f0-9]{64}$/.test(value)) {
    throw new TypeError(`${name} must be a lowercase SHA-256 hex digest.`);
  }
}

function requireIso(value, name) {
  requireText(value, name);

  if (Number.isNaN(Date.parse(value))) {
    throw new TypeError(`${name} must be ISO-8601.`);
  }
}

function requireText(value, name) {
  if (typeof value !== "string" || value.trim().length === 0) {
    throw new TypeError(`${name} is required.`);
  }
}

function changes(result) {
  return Number(result?.meta?.changes ?? 0);
}