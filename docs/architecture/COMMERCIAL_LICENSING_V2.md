# Commercial Licensing V2 architecture contract

## Status and boundary

This document defines the provider-neutral commercial licensing boundary for a
future paid PC-SPA release. It does not activate licensing in the published
Open Beta, introduce live payments, deploy a service, or modify production
Cloudflare data. The current Open Beta remains activation-free for exactly 30
days from each build's official UTC release timestamp.

The legacy password, activation-key, controlled-Beta invitation, device-token,
and licence-token systems are not reusable foundations. Commercial Licensing
V2 must not restore them.

## Confirmed customer policy

- Existing website accounts are retained.
- Authentication is passwordless and browser-based.
- Transactional magic links are sent through Resend from
  `support@getpcspa.com` after domain authentication is verified.
- One purchased seat permits one active Windows device.
- Same-device reinstallation does not consume a transfer.
- A customer may perform two self-service device transfers in a rolling
  12-month period, with a seven-day cooldown between transfers.
- Genuine device failures may receive a permission-controlled, audited support
  override.
- A signed entitlement may be cached for 30 days of offline use, with warnings
  during the final seven days.
- Failed subscription renewal has a separate seven-day payment grace period.
  Existing activated devices continue during grace; new activations and device
  transfers are blocked.
- Existing legacy licence, activation, device, controlled-Beta invitation, and
  controlled-Beta entitlement records are not migrated.

## Source of truth

The payment provider is not the licence server. Verified provider events update
internal orders and subscriptions. Internal entitlement state is the sole
commercial source of truth used to decide whether a device may activate,
continue, transfer, download, or update.

The canonical flow is:

1. Hosted checkout completes at the payment provider.
2. The backend verifies the signed webhook and stores its durable event ID.
3. Idempotent processing updates the order and subscription.
4. The backend creates, renews, limits, suspends, or revokes the entitlement.
5. An authenticated customer authorizes a specific installation in a browser.
6. The server validates entitlement and seat availability.
7. The server registers the privacy-conscious device identifier and issues a
   short-lived authorization result followed by a signed offline entitlement.
8. The desktop verifies the signature and stores only the minimum credential
   required for offline operation.

A browser success page, client-supplied price, checkout result, plan, or payment
status must never issue an entitlement by itself.

## Canonical records

Commercial V2 uses new records with explicit ownership and lifecycle:

- account
- product and plan
- regional price
- order and order line
- payment event and payment attempt
- provider subscription reference
- entitlement
- device registration
- device transfer
- software release and update policy
- audit event

Entitlement states are `pending`, `active`, `grace`, `expired`, `suspended`,
`revoked`, and `refunded`. State transitions must be documented, idempotent,
audited, and covered by tests.

## Security rules

- Use hosted or tokenized checkout; PC-SPA never stores payment-card data.
- Verify webhook signatures before processing an event.
- Store each provider event ID and process it at most once.
- Calculate product, price, currency, seat count, and entitlement server-side.
- Never embed an entitlement-signing secret in the desktop application.
- Sign entitlements asymmetrically so the desktop contains only a public key.
- Use short-lived browser authorization codes that are single-use, bound to the
  requesting installation, and safe against replay.
- Hash or pseudonymize device identifiers and do not collect unnecessary raw
  hardware serial numbers.
- Rate-limit authentication, activation, transfer, and recovery endpoints.
- Require permission checks and audit events for every support override.
- Keep payment grace separate from offline outage tolerance.

## Delivery sequence

1. Remove legacy repository and production licensing remnants while preserving
   accounts and feedback reporting.
2. Define new schemas, state transitions, API contracts, threat model, signing
   key management, and rollback plan.
3. Implement passwordless account authentication and browser authorization in
   a non-production environment.
4. Implement provider-neutral entitlement, device, transfer, audit, and signed
   assertion services using simulated verified payment events.
5. Select and integrate a payment provider through a narrow adapter and
   verified webhook processor.
6. Connect the desktop only after the backend contracts and signing validation
   are proven.
7. Run end-to-end payment, entitlement, recovery, offline, cancellation,
   refund, duplicate-event, delayed-event, and outage tests before production.

## Explicit exclusions from this change

- No payment provider selection or live checkout
- No production deployment or database mutation
- No Cloudflare secret creation or deletion
- No desktop licensing runtime
- No change to Open Beta release or expiry timestamps
- No change to Windows Repair or other optimization tools
- No reuse or migration of legacy licensing records