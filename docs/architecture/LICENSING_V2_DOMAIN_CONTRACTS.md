# Licensing V2 domain contracts

This increment adds provider-neutral, deterministic domain contracts only. It
does not connect a payment gateway, process a webhook, persist commercial data,
send email, register a real device, sign an entitlement, or alter Open Beta.

## Decisions represented in code

- one active device per purchased seat
- same-device reinstall remains available without consuming a transfer
- two self-service transfers in a rolling 365-day window
- seven-day transfer cooldown
- 30-day signed offline period with warning during the final seven days
- seven-day failed-payment grace for existing devices only
- pending, expired, suspended, revoked, and refunded states fail closed
- verified payment events are normalized inputs, not licences

## Trust boundary

`VerifiedPaymentEvent` means a future adapter has already authenticated the
provider webhook. The Core type does not verify provider signatures. A future
backend adapter must verify signatures, reject replays, persist the provider
event ID idempotently, and calculate product, price, currency, seat count, and
account mapping server-side before constructing this contract.

`CommercialLicensePolicy` performs no I/O. It produces separate decisions for
existing-device use, new activation, transfer, offline use, and warning state.
Unknown states and malformed records fail closed.

## Deferred work

- database schema and migrations
- idempotent payment-event receipt store
- entitlement state-transition service
- browser and magic-link authentication
- privacy-conscious device identity
- asymmetric entitlement signing and key rotation
- payment-provider selection and adapter
- Cloudflare deployment and production migration
- desktop licensing integration