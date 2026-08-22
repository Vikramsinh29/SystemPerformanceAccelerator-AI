PRAGMA foreign_keys = ON;

-- PC-SPA one-time browser-to-desktop installation authorization.
--
-- Security properties:
--   * authorization codes are opaque random values;
--   * plaintext authorization codes are never persisted;
--   * only SHA-256 code digests are stored;
--   * codes are short-lived;
--   * successful exchange atomically records consumed_utc;
--   * the trusted account/product identity is bound at issuance time.

CREATE TABLE installation_authorizations (
  authorization_id TEXT PRIMARY KEY,

  code_sha256 TEXT NOT NULL UNIQUE CHECK (
    length(code_sha256) = 64
    AND code_sha256 = lower(code_sha256)
  ),

  account_id TEXT NOT NULL CHECK (
    length(trim(account_id)) BETWEEN 1 AND 128
  ),

  product_id TEXT NOT NULL CHECK (
    length(trim(product_id)) BETWEEN 1 AND 128
  ),

  created_utc TEXT NOT NULL,

  expires_utc TEXT NOT NULL CHECK (
    expires_utc > created_utc
  ),

  consumed_utc TEXT CHECK (
    consumed_utc IS NULL OR consumed_utc >= created_utc
  )
);

CREATE INDEX ix_installation_authorizations_expiry
  ON installation_authorizations(expires_utc);

CREATE INDEX ix_installation_authorizations_account
  ON installation_authorizations(account_id, product_id, created_utc);