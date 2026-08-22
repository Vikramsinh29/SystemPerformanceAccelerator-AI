PRAGMA foreign_keys = ON;

-- PC-SPA Commercial Pricing & Packaging
-- Authoritative Price Book Version 1
-- Approved: 2026-08-22
--
-- IMPORTANT:
-- Prices in an active price book are immutable commercial records.
-- Future pricing changes MUST create a new price-book version.
-- Historical versions must never be overwritten.

CREATE TABLE commercial_price_books (
  price_book_id TEXT PRIMARY KEY,
  version INTEGER NOT NULL UNIQUE CHECK (version >= 1),
  status TEXT NOT NULL CHECK (
    status IN ('draft', 'active', 'retired')
  ),
  effective_from_utc TEXT NOT NULL,
  effective_to_utc TEXT,
  created_utc TEXT NOT NULL,
  created_by TEXT NOT NULL,
  change_reason TEXT NOT NULL
);

CREATE UNIQUE INDEX ux_commercial_price_book_active
  ON commercial_price_books(status)
  WHERE status = 'active';

CREATE TABLE commercial_price_book_entries (
  price_entry_id TEXT PRIMARY KEY,
  price_book_id TEXT NOT NULL,

  market_code TEXT NOT NULL CHECK (
    market_code IN ('IN', 'GLOBAL')
  ),

  currency TEXT NOT NULL CHECK (
    length(currency) = 3
    AND currency = upper(currency)
  ),

  customer_segment TEXT NOT NULL CHECK (
    customer_segment IN ('free', 'pro', 'business')
  ),

  plan_code TEXT NOT NULL,

  billing_interval TEXT NOT NULL CHECK (
    billing_interval IN ('none', 'monthly', 'annual')
  ),

  price_phase TEXT NOT NULL CHECK (
    price_phase IN ('standard', 'introductory')
  ),

  pricing_mode TEXT NOT NULL CHECK (
    pricing_mode IN ('fixed', 'per_seat', 'contact_sales')
  ),

  min_seats INTEGER NOT NULL CHECK (
    min_seats >= 1
  ),

  max_seats INTEGER NOT NULL CHECK (
    max_seats >= min_seats
  ),

  unit_amount_minor INTEGER,

  first_cycle_only INTEGER NOT NULL DEFAULT 0 CHECK (
    first_cycle_only IN (0, 1)
  ),

  created_utc TEXT NOT NULL,

  FOREIGN KEY (price_book_id)
    REFERENCES commercial_price_books(price_book_id),

  CHECK (
    (pricing_mode IN ('fixed', 'per_seat')
      AND unit_amount_minor IS NOT NULL
      AND unit_amount_minor >= 0)
    OR
    (pricing_mode = 'contact_sales'
      AND unit_amount_minor IS NULL)
  ),

  UNIQUE (
    price_book_id,
    market_code,
    plan_code,
    billing_interval,
    price_phase,
    min_seats,
    max_seats
  )
);

CREATE INDEX ix_price_book_entries_lookup
  ON commercial_price_book_entries(
    price_book_id,
    market_code,
    customer_segment,
    billing_interval,
    min_seats,
    max_seats
  );

INSERT INTO commercial_price_books (
  price_book_id,
  version,
  status,
  effective_from_utc,
  effective_to_utc,
  created_utc,
  created_by,
  change_reason
)
VALUES (
  'pcspa-commercial-v1',
  1,
  'active',
  '2026-08-22T00:00:00Z',
  NULL,
  '2026-08-22T00:00:00Z',
  'owner-approved',
  'Initial authoritative PC-SPA India and worldwide Commercial Pricing & Packaging v1.'
);

-- =========================================================
-- INDIA
-- Amounts are in INR minor units (paise)
-- =========================================================

INSERT INTO commercial_price_book_entries VALUES
(
  'v1-in-free',
  'pcspa-commercial-v1',
  'IN',
  'INR',
  'free',
  'PCSPA_FREE',
  'none',
  'standard',
  'fixed',
  1,
  1,
  0,
  0,
  '2026-08-22T00:00:00Z'
),

(
  'v1-in-pro-monthly',
  'pcspa-commercial-v1',
  'IN',
  'INR',
  'pro',
  'PCSPA_PRO',
  'monthly',
  'standard',
  'fixed',
  1,
  1,
  14900,
  0,
  '2026-08-22T00:00:00Z'
),

(
  'v1-in-pro-annual-standard',
  'pcspa-commercial-v1',
  'IN',
  'INR',
  'pro',
  'PCSPA_PRO',
  'annual',
  'standard',
  'fixed',
  1,
  1,
  119900,
  0,
  '2026-08-22T00:00:00Z'
),

(
  'v1-in-pro-annual-intro',
  'pcspa-commercial-v1',
  'IN',
  'INR',
  'pro',
  'PCSPA_PRO',
  'annual',
  'introductory',
  'fixed',
  1,
  1,
  89900,
  1,
  '2026-08-22T00:00:00Z'
),

-- India Business Monthly

(
  'v1-in-business-monthly-5-9',
  'pcspa-commercial-v1',
  'IN',
  'INR',
  'business',
  'PCSPA_BUSINESS',
  'monthly',
  'standard',
  'per_seat',
  5,
  9,
  11900,
  0,
  '2026-08-22T00:00:00Z'
),

(
  'v1-in-business-monthly-10-24',
  'pcspa-commercial-v1',
  'IN',
  'INR',
  'business',
  'PCSPA_BUSINESS',
  'monthly',
  'standard',
  'per_seat',
  10,
  24,
  10900,
  0,
  '2026-08-22T00:00:00Z'
),

(
  'v1-in-business-monthly-25-49',
  'pcspa-commercial-v1',
  'IN',
  'INR',
  'business',
  'PCSPA_BUSINESS',
  'monthly',
  'standard',
  'per_seat',
  25,
  49,
  9900,
  0,
  '2026-08-22T00:00:00Z'
),

(
  'v1-in-business-monthly-50-99',
  'pcspa-commercial-v1',
  'IN',
  'INR',
  'business',
  'PCSPA_BUSINESS',
  'monthly',
  'standard',
  'per_seat',
  50,
  99,
  8900,
  0,
  '2026-08-22T00:00:00Z'
),

(
  'v1-in-business-monthly-100-plus',
  'pcspa-commercial-v1',
  'IN',
  'INR',
  'business',
  'PCSPA_BUSINESS',
  'monthly',
  'standard',
  'contact_sales',
  100,
  100000,
  NULL,
  0,
  '2026-08-22T00:00:00Z'
),

-- India Business Annual

(
  'v1-in-business-annual-5-9',
  'pcspa-commercial-v1',
  'IN',
  'INR',
  'business',
  'PCSPA_BUSINESS',
  'annual',
  'standard',
  'per_seat',
  5,
  9,
  89900,
  0,
  '2026-08-22T00:00:00Z'
),

(
  'v1-in-business-annual-10-24',
  'pcspa-commercial-v1',
  'IN',
  'INR',
  'business',
  'PCSPA_BUSINESS',
  'annual',
  'standard',
  'per_seat',
  10,
  24,
  84900,
  0,
  '2026-08-22T00:00:00Z'
),

(
  'v1-in-business-annual-25-49',
  'pcspa-commercial-v1',
  'IN',
  'INR',
  'business',
  'PCSPA_BUSINESS',
  'annual',
  'standard',
  'per_seat',
  25,
  49,
  79900,
  0,
  '2026-08-22T00:00:00Z'
),

(
  'v1-in-business-annual-50-99',
  'pcspa-commercial-v1',
  'IN',
  'INR',
  'business',
  'PCSPA_BUSINESS',
  'annual',
  'standard',
  'per_seat',
  50,
  99,
  74900,
  0,
  '2026-08-22T00:00:00Z'
),

(
  'v1-in-business-annual-100-plus',
  'pcspa-commercial-v1',
  'IN',
  'INR',
  'business',
  'PCSPA_BUSINESS',
  'annual',
  'standard',
  'contact_sales',
  100,
  100000,
  NULL,
  0,
  '2026-08-22T00:00:00Z'
);

-- =========================================================
-- WORLDWIDE DEFAULT
-- Amounts are in USD minor units (cents)
-- =========================================================

INSERT INTO commercial_price_book_entries VALUES
(
  'v1-global-free',
  'pcspa-commercial-v1',
  'GLOBAL',
  'USD',
  'free',
  'PCSPA_FREE',
  'none',
  'standard',
  'fixed',
  1,
  1,
  0,
  0,
  '2026-08-22T00:00:00Z'
),

(
  'v1-global-pro-monthly',
  'pcspa-commercial-v1',
  'GLOBAL',
  'USD',
  'pro',
  'PCSPA_PRO',
  'monthly',
  'standard',
  'fixed',
  1,
  1,
  299,
  0,
  '2026-08-22T00:00:00Z'
),

(
  'v1-global-pro-annual-standard',
  'pcspa-commercial-v1',
  'GLOBAL',
  'USD',
  'pro',
  'PCSPA_PRO',
  'annual',
  'standard',
  'fixed',
  1,
  1,
  2499,
  0,
  '2026-08-22T00:00:00Z'
),

(
  'v1-global-pro-annual-intro',
  'pcspa-commercial-v1',
  'GLOBAL',
  'USD',
  'pro',
  'PCSPA_PRO',
  'annual',
  'introductory',
  'fixed',
  1,
  1,
  1999,
  1,
  '2026-08-22T00:00:00Z'
),

-- Worldwide Business Monthly

(
  'v1-global-business-monthly-5-9',
  'pcspa-commercial-v1',
  'GLOBAL',
  'USD',
  'business',
  'PCSPA_BUSINESS',
  'monthly',
  'standard',
  'per_seat',
  5,
  9,
  249,
  0,
  '2026-08-22T00:00:00Z'
),

(
  'v1-global-business-monthly-10-24',
  'pcspa-commercial-v1',
  'GLOBAL',
  'USD',
  'business',
  'PCSPA_BUSINESS',
  'monthly',
  'standard',
  'per_seat',
  10,
  24,
  229,
  0,
  '2026-08-22T00:00:00Z'
),

(
  'v1-global-business-monthly-25-49',
  'pcspa-commercial-v1',
  'GLOBAL',
  'USD',
  'business',
  'PCSPA_BUSINESS',
  'monthly',
  'standard',
  'per_seat',
  25,
  49,
  209,
  0,
  '2026-08-22T00:00:00Z'
),

(
  'v1-global-business-monthly-50-99',
  'pcspa-commercial-v1',
  'GLOBAL',
  'USD',
  'business',
  'PCSPA_BUSINESS',
  'monthly',
  'standard',
  'per_seat',
  50,
  99,
  189,
  0,
  '2026-08-22T00:00:00Z'
),

(
  'v1-global-business-monthly-100-plus',
  'pcspa-commercial-v1',
  'GLOBAL',
  'USD',
  'business',
  'PCSPA_BUSINESS',
  'monthly',
  'standard',
  'contact_sales',
  100,
  100000,
  NULL,
  0,
  '2026-08-22T00:00:00Z'
),

-- Worldwide Business Annual

(
  'v1-global-business-annual-5-9',
  'pcspa-commercial-v1',
  'GLOBAL',
  'USD',
  'business',
  'PCSPA_BUSINESS',
  'annual',
  'standard',
  'per_seat',
  5,
  9,
  1999,
  0,
  '2026-08-22T00:00:00Z'
),

(
  'v1-global-business-annual-10-24',
  'pcspa-commercial-v1',
  'GLOBAL',
  'USD',
  'business',
  'PCSPA_BUSINESS',
  'annual',
  'standard',
  'per_seat',
  10,
  24,
  1799,
  0,
  '2026-08-22T00:00:00Z'
),

(
  'v1-global-business-annual-25-49',
  'pcspa-commercial-v1',
  'GLOBAL',
  'USD',
  'business',
  'PCSPA_BUSINESS',
  'annual',
  'standard',
  'per_seat',
  25,
  49,
  1699,
  0,
  '2026-08-22T00:00:00Z'
),

(
  'v1-global-business-annual-50-99',
  'pcspa-commercial-v1',
  'GLOBAL',
  'USD',
  'business',
  'PCSPA_BUSINESS',
  'annual',
  'standard',
  'per_seat',
  50,
  99,
  1599,
  0,
  '2026-08-22T00:00:00Z'
),

(
  'v1-global-business-annual-100-plus',
  'pcspa-commercial-v1',
  'GLOBAL',
  'USD',
  'business',
  'PCSPA_BUSINESS',
  'annual',
  'standard',
  'contact_sales',
  100,
  100000,
  NULL,
  0,
  '2026-08-22T00:00:00Z'
);