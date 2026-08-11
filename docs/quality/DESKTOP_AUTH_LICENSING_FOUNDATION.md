# Desktop Auth And Licensing Foundation

## Scope

Phase 1 adds the desktop-side integration foundation for the deployed authentication and licensing backend without changing the existing WPF screens or installer flow.

## Configuration

- Production API base URL defaults to `https://pc-spa-web.pc-spa-feedback.workers.dev/`
- Development API base URL defaults to `https://localhost:8787/`
- `PC_SPA_API_ENVIRONMENT` selects `Development` or `Production`
- `PC_SPA_API_BASE_URL` optionally overrides the active environment URL
- `PC_SPA_API_TIMEOUT_SECONDS` optionally overrides the request timeout
- No secrets are stored in source control

## Device Identity Privacy

- PC-SPA does not transmit raw hardware serial numbers
- PC-SPA derives a stable application-specific device ID locally
- The derived ID uses a SHA-256 hash over the Windows machine GUID, the current Windows user SID, and an application namespace
- The raw machine GUID and SID remain local and are not sent to the backend
- If the local machine GUID or SID cannot be read, PC-SPA falls back to a locally persisted derived identifier

## Secure Storage

- Passwords are never stored locally
- Raw activation keys are never stored after successful activation
- Only the issued session token and license token are persisted
- Token persistence uses Windows DPAPI through the existing credential protection mechanism
- Persisted token files are written atomically through a temporary file and replace flow

## Retry Policy

- Safe transient retry is enabled only for session lookup and license validation
- Login, logout, activation, and deactivation do not retry automatically
- Authentication and validation failures are never retried automatically
