PRAGMA foreign_keys = ON;

CREATE TABLE billing_customers (
  customer_id TEXT PRIMARY KEY,
  account_id TEXT NOT NULL UNIQUE,
  status TEXT NOT NULL CHECK (status IN (
    'active', 'disabled'
  )),
  created_utc TEXT NOT NULL,
  updated_utc TEXT NOT NULL
);

CREATE TABLE billing_plans (
  plan_id TEXT PRIMARY KEY,
  product_id TEXT NOT NULL,
  plan_code TEXT NOT NULL UNIQUE,
  billing_interval TEXT NOT NULL CHECK (billing_interval IN (
    'monthly', 'annual'
  )),
  seat_limit INTEGER NOT NULL CHECK (seat_limit >= 1),
  status TEXT NOT NULL CHECK (status IN (
    'draft', 'active', 'retired'
  )),
  created_utc TEXT NOT NULL,
  updated_utc TEXT NOT NULL
);

CREATE TABLE billing_prices (
  price_id TEXT PRIMARY KEY,
  plan_id TEXT NOT NULL,
  currency TEXT NOT NULL CHECK (
    length(currency) = 3 AND currency = upper(currency)
  ),
  unit_amount_minor INTEGER NOT NULL CHECK (unit_amount_minor >= 0),
  tax_mode TEXT NOT NULL CHECK (tax_mode IN (
    'provider_managed', 'inclusive', 'exclusive'
  )),
  status TEXT NOT NULL CHECK (status IN (
    'draft', 'active', 'retired'
  )),
  created_utc TEXT NOT NULL,
  updated_utc TEXT NOT NULL,
  UNIQUE (plan_id, currency),
  FOREIGN KEY (plan_id) REFERENCES billing_plans(plan_id)
);

CREATE TABLE billing_subscriptions (
  subscription_id TEXT PRIMARY KEY,
  customer_id TEXT NOT NULL,
  product_id TEXT NOT NULL,
  plan_id TEXT NOT NULL,
  price_id TEXT NOT NULL,
  state TEXT NOT NULL CHECK (state IN (
    'pending',
    'active',
    'past_due',
    'grace',
    'canceled',
    'expired',
    'suspended'
  )),
  period_starts_utc TEXT,
  period_ends_utc TEXT,
  payment_grace_ends_utc TEXT,
  last_provider_event_utc TEXT,
  cancel_at_period_end INTEGER NOT NULL DEFAULT 0 CHECK (
    cancel_at_period_end IN (0, 1)
  ),
  canceled_utc TEXT,
  version INTEGER NOT NULL DEFAULT 0 CHECK (version >= 0),
  created_utc TEXT NOT NULL,
  updated_utc TEXT NOT NULL,
  FOREIGN KEY (customer_id) REFERENCES billing_customers(customer_id),
  FOREIGN KEY (plan_id) REFERENCES billing_plans(plan_id),
  FOREIGN KEY (price_id) REFERENCES billing_prices(price_id)
);

CREATE TABLE billing_transactions (
  transaction_id TEXT PRIMARY KEY,
  customer_id TEXT NOT NULL,
  subscription_id TEXT,
  transaction_kind TEXT NOT NULL CHECK (transaction_kind IN (
    'charge',
    'refund',
    'chargeback',
    'adjustment'
  )),
  status TEXT NOT NULL CHECK (status IN (
    'pending',
    'paid',
    'failed',
    'partially_refunded',
    'refunded',
    'charged_back'
  )),
  currency TEXT NOT NULL CHECK (
    length(currency) = 3 AND currency = upper(currency)
  ),
  list_amount_minor INTEGER NOT NULL DEFAULT 0 CHECK (
    list_amount_minor >= 0
  ),
  discount_amount_minor INTEGER NOT NULL DEFAULT 0 CHECK (
    discount_amount_minor >= 0
  ),
  subtotal_minor INTEGER NOT NULL DEFAULT 0 CHECK (
    subtotal_minor >= 0
  ),
  tax_amount_minor INTEGER NOT NULL DEFAULT 0 CHECK (
    tax_amount_minor >= 0
  ),
  gross_amount_minor INTEGER NOT NULL DEFAULT 0 CHECK (
    gross_amount_minor >= 0
  ),
  processor_fee_minor INTEGER NOT NULL DEFAULT 0 CHECK (
    processor_fee_minor >= 0
  ),
  processor_fee_tax_minor INTEGER NOT NULL DEFAULT 0 CHECK (
    processor_fee_tax_minor >= 0
  ),
  refund_amount_minor INTEGER NOT NULL DEFAULT 0 CHECK (
    refund_amount_minor >= 0
  ),
  chargeback_amount_minor INTEGER NOT NULL DEFAULT 0 CHECK (
    chargeback_amount_minor >= 0
  ),
  net_receivable_minor INTEGER NOT NULL DEFAULT 0,
  occurred_utc TEXT NOT NULL,
  settled_utc TEXT,
  created_utc TEXT NOT NULL,
  updated_utc TEXT NOT NULL,
  FOREIGN KEY (customer_id) REFERENCES billing_customers(customer_id),
  FOREIGN KEY (subscription_id)
    REFERENCES billing_subscriptions(subscription_id)
);

CREATE TABLE billing_provider_events (
  provider TEXT NOT NULL,
  provider_event_id TEXT NOT NULL,
  event_type TEXT NOT NULL,
  occurred_utc TEXT NOT NULL,
  received_utc TEXT NOT NULL,
  payload_sha256 TEXT NOT NULL,
  processing_status TEXT NOT NULL CHECK (processing_status IN (
    'received',
    'processed',
    'ignored',
    'retryable_failure',
    'rejected'
  )),
  attempt_count INTEGER NOT NULL DEFAULT 1 CHECK (attempt_count >= 1),
  last_error_code TEXT,
  processed_utc TEXT,
  PRIMARY KEY (provider, provider_event_id)
) WITHOUT ROWID;

CREATE TABLE billing_provider_mappings (
  mapping_id TEXT PRIMARY KEY,
  provider TEXT NOT NULL,
  entity_type TEXT NOT NULL CHECK (entity_type IN (
    'customer',
    'plan',
    'price',
    'subscription',
    'transaction'
  )),
  internal_entity_id TEXT NOT NULL,
  provider_ref TEXT NOT NULL,
  created_utc TEXT NOT NULL,
  updated_utc TEXT NOT NULL,
  UNIQUE (provider, provider_ref),
  UNIQUE (provider, entity_type, internal_entity_id)
);

CREATE TABLE billing_ledger_entries (
  ledger_entry_id TEXT PRIMARY KEY,
  transaction_id TEXT NOT NULL,
  entry_type TEXT NOT NULL CHECK (entry_type IN (
    'list_price',
    'discount',
    'subtotal',
    'tax',
    'gross',
    'processor_fee',
    'processor_fee_tax',
    'refund',
    'chargeback',
    'net_receivable',
    'adjustment'
  )),
  currency TEXT NOT NULL CHECK (
    length(currency) = 3 AND currency = upper(currency)
  ),
  amount_minor INTEGER NOT NULL,
  occurred_utc TEXT NOT NULL,
  created_utc TEXT NOT NULL,
  FOREIGN KEY (transaction_id)
    REFERENCES billing_transactions(transaction_id)
);

CREATE INDEX ix_billing_subscriptions_customer
  ON billing_subscriptions(customer_id, state);

CREATE INDEX ix_billing_subscriptions_period_end
  ON billing_subscriptions(state, period_ends_utc);

CREATE INDEX ix_billing_transactions_subscription
  ON billing_transactions(subscription_id, occurred_utc);

CREATE INDEX ix_billing_transactions_customer
  ON billing_transactions(customer_id, occurred_utc);

CREATE INDEX ix_billing_provider_events_processing
  ON billing_provider_events(processing_status, received_utc);

CREATE INDEX ix_billing_provider_mappings_internal
  ON billing_provider_mappings(entity_type, internal_entity_id);

CREATE INDEX ix_billing_ledger_transaction
  ON billing_ledger_entries(transaction_id, occurred_utc);