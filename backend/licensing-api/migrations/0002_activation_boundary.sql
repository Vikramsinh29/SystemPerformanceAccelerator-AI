PRAGMA foreign_keys = ON;

CREATE TABLE licensing_devices (
  device_id TEXT PRIMARY KEY,
  entitlement_id TEXT NOT NULL,
  device_fingerprint_hash TEXT NOT NULL,
  display_name TEXT,
  first_activated_utc TEXT NOT NULL,
  last_validated_utc TEXT,
  deactivated_utc TEXT,
  status TEXT NOT NULL CHECK (status IN ('active', 'deactivated')),
  UNIQUE (entitlement_id, device_fingerprint_hash),
  FOREIGN KEY (entitlement_id)
    REFERENCES licensing_entitlements(entitlement_id)
);

CREATE TABLE licensing_activation_events (
  activation_id TEXT PRIMARY KEY,
  entitlement_id TEXT NOT NULL,
  device_id TEXT NOT NULL,
  action TEXT NOT NULL CHECK (action IN (
    'activate', 'reinstall', 'validate', 'deactivate', 'transfer_in', 'transfer_out'
  )),
  occurred_utc TEXT NOT NULL,
  result TEXT NOT NULL CHECK (result IN ('allowed', 'blocked')),
  reason_code TEXT NOT NULL,
  request_id TEXT NOT NULL UNIQUE,
  FOREIGN KEY (entitlement_id)
    REFERENCES licensing_entitlements(entitlement_id),
  FOREIGN KEY (device_id)
    REFERENCES licensing_devices(device_id)
);

CREATE TABLE licensing_transfer_events (
  transfer_id TEXT PRIMARY KEY,
  entitlement_id TEXT NOT NULL,
  from_device_id TEXT,
  to_device_id TEXT,
  occurred_utc TEXT NOT NULL,
  window_started_utc TEXT NOT NULL,
  transfer_ordinal INTEGER NOT NULL CHECK (transfer_ordinal >= 1),
  FOREIGN KEY (entitlement_id)
    REFERENCES licensing_entitlements(entitlement_id),
  FOREIGN KEY (from_device_id)
    REFERENCES licensing_devices(device_id),
  FOREIGN KEY (to_device_id)
    REFERENCES licensing_devices(device_id)
);

CREATE TABLE licensing_offline_assertions (
  assertion_id TEXT PRIMARY KEY,
  entitlement_id TEXT NOT NULL,
  device_id TEXT NOT NULL,
  issued_utc TEXT NOT NULL,
  expires_utc TEXT NOT NULL,
  key_id TEXT NOT NULL,
  assertion_hash TEXT NOT NULL UNIQUE,
  revoked_utc TEXT,
  FOREIGN KEY (entitlement_id)
    REFERENCES licensing_entitlements(entitlement_id),
  FOREIGN KEY (device_id)
    REFERENCES licensing_devices(device_id)
);

CREATE INDEX ix_licensing_devices_entitlement_status
  ON licensing_devices(entitlement_id, status);
CREATE INDEX ix_licensing_activation_entitlement_time
  ON licensing_activation_events(entitlement_id, occurred_utc);
CREATE INDEX ix_licensing_transfer_entitlement_time
  ON licensing_transfer_events(entitlement_id, occurred_utc);
CREATE INDEX ix_licensing_assertions_device_expiry
  ON licensing_offline_assertions(device_id, expires_utc);
