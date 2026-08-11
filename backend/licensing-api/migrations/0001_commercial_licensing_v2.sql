PRAGMA foreign_keys = ON;

CREATE TABLE licensing_payment_event_receipts (
  provider TEXT NOT NULL,
  provider_event_id TEXT NOT NULL,
  account_id TEXT NOT NULL,
  product_id TEXT NOT NULL,
  provider_subscription_id TEXT,
  event_kind TEXT NOT NULL,
  occurred_utc TEXT NOT NULL,
  processing_status TEXT NOT NULL CHECK (processing_status IN (
    'retryable_failure', 'processed'
  )),
  attempt_count INTEGER NOT NULL DEFAULT 1 CHECK (attempt_count >= 1),
  last_error_code TEXT,
  first_received_utc TEXT NOT NULL,
  last_attempt_utc TEXT NOT NULL,
  processed_utc TEXT,
  PRIMARY KEY (provider, provider_event_id)
) WITHOUT ROWID;

CREATE TABLE licensing_entitlements (
  entitlement_id TEXT PRIMARY KEY,
  account_id TEXT NOT NULL,
  product_id TEXT NOT NULL,
  state TEXT NOT NULL CHECK (state IN (
    'pending', 'active', 'grace', 'expired', 'suspended', 'revoked', 'refunded'
  )),
  seat_limit INTEGER NOT NULL CHECK (seat_limit >= 1),
  active_device_count INTEGER NOT NULL DEFAULT 0 CHECK (
    active_device_count >= 0 AND active_device_count <= seat_limit
  ),
  period_ends_utc TEXT NOT NULL,
  payment_grace_ends_utc TEXT,
  offline_valid_until_utc TEXT,
  transfers_used INTEGER NOT NULL DEFAULT 0 CHECK (transfers_used >= 0),
  transfer_window_started_utc TEXT NOT NULL,
  last_transfer_utc TEXT,
  last_commercial_event_utc TEXT,
  version INTEGER NOT NULL DEFAULT 0 CHECK (version >= 0),
  updated_utc TEXT NOT NULL,
  UNIQUE (account_id, product_id)
);

CREATE TABLE licensing_audit_events (
  audit_id TEXT PRIMARY KEY,
  occurred_utc TEXT NOT NULL,
  provider TEXT NOT NULL,
  provider_event_id TEXT NOT NULL,
  account_id TEXT NOT NULL,
  product_id TEXT NOT NULL,
  event_kind TEXT NOT NULL,
  processing_outcome TEXT NOT NULL CHECK (processing_outcome IN (
    'applied', 'ignored_out_of_order', 'rejected'
  )),
  previous_state TEXT,
  current_state TEXT,
  message TEXT NOT NULL,
  FOREIGN KEY (provider, provider_event_id)
    REFERENCES licensing_payment_event_receipts(provider, provider_event_id)
);

CREATE INDEX ix_licensing_receipts_retry
  ON licensing_payment_event_receipts(processing_status, last_attempt_utc);
CREATE INDEX ix_licensing_entitlements_account
  ON licensing_entitlements(account_id, state);
CREATE INDEX ix_licensing_audit_account_time
  ON licensing_audit_events(account_id, occurred_utc);
