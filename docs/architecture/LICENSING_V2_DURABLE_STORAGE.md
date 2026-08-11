# Licensing V2 durable-storage foundation

## Boundary

Commercial licensing storage is a separate backend module. It is not added to
the feedback Worker or the `pc-spa-feedback` database. The configuration uses a
non-production placeholder D1 identifier and must not be deployed.

The currently deployed `pc-spa-web` account Worker is not represented in this
repository. Its source, schema and account identifiers must be recovered and
reviewed before this module is connected to retained customer accounts.

## Transaction contract

Each accepted commercial transition is committed through one D1 `batch`:

1. insert the provider event receipt using its provider-scoped unique ID
2. insert or version-guard the entitlement mutation
3. append the corresponding immutable audit event

Dependent writes require the processed receipt to exist. Existing entitlement
updates use optimistic concurrency through the `version` column. Callers must
retry after a version conflict by reading the current entitlement and
recalculating the transition. D1 batch failure must be treated as an entirely
failed transaction.

Retryable failures retain an attempt counter and error code. A failure update
cannot overwrite a processed receipt.

## Deliberate exclusions

- no remote D1 database or migration
- no Cloudflare deployment or route
- no payment-provider webhook or signature verification
- no account-worker integration
- no production secret or signing key
- no desktop activation endpoint
- no device or transfer persistence

Before production, replace the placeholder configuration, connect canonical
account IDs, test the migration on a disposable preview database, add recovery
monitoring, and prove duplicate, delayed, concurrent and rollback behavior.
