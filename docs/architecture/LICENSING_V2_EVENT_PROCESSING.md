# Licensing V2 simulated event processing

This increment proves provider-neutral state transitions and duplicate-event
handling entirely in memory. It is executable design evidence, not a production
payment processor.

## Implemented behavior

- purchase and successful renewal activate or renew an entitlement
- renewal failure begins the approved seven-day payment grace
- cancellation preserves access until the recorded paid period ends
- refund changes the entitlement to Refunded
- dispute changes the entitlement to Suspended
- duplicate provider event IDs are not applied twice
- older out-of-order events cannot overwrite newer entitlement state
- every non-duplicate processing outcome produces an audit record
- invalid normalized events and impossible transitions fail closed

## Production boundary

`SimulatedPaymentEventProcessor` deliberately uses process memory. It must not
be registered as a production service. A production implementation requires a
durable database transaction that atomically records the provider event,
applies the entitlement transition, and appends the audit record. Failed
transactions must be safely retryable.

Before constructing `VerifiedPaymentEvent`, a future provider adapter must
verify the webhook signature and calculate account, product, price, currency,
seat count, subscription, and period data from trusted server-side records.

## Deferred work

- D1 schema and transaction design
- durable event receipt and retry states
- verified webhook adapter
- authentication and account mapping
- signing keys and offline entitlement issuance
- device registration and transfer persistence
- production monitoring, recovery, and deployment