# PC-SPA Licensing V2 — Staging Boundary

## Status

Implementation boundary only. No production deployment is authorized by this document.

## Purpose

Define the minimum separation required before Commercial Licensing V2 can be exercised in a non-production Cloudflare environment.

## Environment model

- `local` — developer-only Worker and local D1 simulation.
- `staging` — isolated Cloudflare Worker and dedicated staging D1 database.
- `production` — intentionally absent from source configuration until staging acceptance and explicit production approval.

The staging and future production licensing databases must not reuse the existing `pc-spa` or `pc-spa-feedback` databases.

## Service ownership

### pc-spa-web

Owns browser-facing website and account/session responsibilities. It must not become the authoritative licensing database.

### pc-spa-feedback-api

Owns privacy-safe feedback intake only. It must remain independent of commercial licensing.

### pc-spa-licensing-api

Future owner of authoritative commercial entitlement state, device registrations, activation history, transfer history, payment-event receipts, licensing audit events, and offline assertion records.

## API boundary

Versioned route contracts are reserved for:

- internal entitlement lookup;
- verified payment-event ingestion;
- desktop activation;
- desktop validation;
- desktop transfer.

The current Worker deliberately returns `503 not_deployed`. Route contracts do not authorize public exposure.

## Authentication boundary

Internal browser/account-to-licensing calls must use authenticated service-to-service communication. A mere Bearer-header presence check is not authentication and is explicitly excluded.

Desktop calls require a separately designed request-authentication and abuse-control mechanism before activation endpoints are enabled.

No service secret, signing key, provider secret, or production identifier may be committed to source control.

## Durable storage boundary

The schema now separates:

- verified provider event receipts;
- entitlement state;
- licensing audit events;
- devices;
- activation events;
- transfer events;
- offline assertion records.

Device identity stores only a privacy-conscious fingerprint hash, not raw hardware identifiers.

## Staging acceptance gates

Before any production resource is created:

1. Replace the staging placeholder D1 ID with a dedicated staging database identifier outside production.
2. Apply migrations only to staging.
3. Run all licensing-api tests.
4. Validate Wrangler configuration with the installed project schema.
5. Prove payment-event idempotency and entitlement optimistic concurrency.
6. Prove same-device reinstall does not consume a transfer.
7. Prove transfer cooldown and rolling-window limits.
8. Prove payment-grace restrictions.
9. Prove offline assertions expire and can be revoked.
10. Prove account-service calls cannot bypass licensing authorization.
11. Document staging rollback and database restore.
12. Obtain explicit approval before adding any `production` environment configuration.

## Explicit exclusions

This sprint does not:

- create or migrate a production D1 database;
- deploy a Worker;
- change routes or DNS;
- add or rotate secrets;
- integrate Razorpay or another payment provider;
- enable desktop activation;
- change the current 30-day Open Beta policy;
- modify the live `pc-spa-web` Worker;
- remove legacy live bindings or tables.

## Rollback principle

Until production cutover is explicitly approved, the live website and desktop Beta remain independent of this Licensing V2 staging boundary. Removing the staging Worker or staging database must not affect current users.
