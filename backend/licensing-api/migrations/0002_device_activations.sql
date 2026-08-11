PRAGMA foreign_keys = ON;

CREATE TABLE licensing_device_activations (
  activation_id TEXT PRIMARY KEY,
  entitlement_id TEXT NOT NULL,
  account_id TEXT NOT NULL,
  product_id TEXT NOT NULL,
  device_fingerprint_hash TEXT NOT NULL,
  device_label TEXT,
  status TEXT NOT NULL CHECK (status IN ('active', 'deactivated', 'revoked')),
  activated_utc TEXT NOT NULL,
  last_validated_utc TEXT NOT NULL,
  deactivated_utc TEXT,
  revoked_utc TEXT,
  version INTEGER NOT NULL DEFAULT 0 CHECK (version >= 0),
  FOREIGN KEY (entitlement_id) REFERENCES licensing_entitlements(entitlement_id)
);

CREATE UNIQUE INDEX ux_licensing_device_active_fingerprint
  ON licensing_device_activations(entitlement_id, device_fingerprint_hash)
  WHERE status = 'active';

CREATE INDEX ix_licensing_device_entitlement_status
  ON licensing_device_activations(entitlement_id, status);

CREATE INDEX ix_licensing_device_account_product
  ON licensing_device_activations(account_id, product_id, status);
