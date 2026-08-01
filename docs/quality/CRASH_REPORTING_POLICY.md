# PC-SPA Crash Reporting Policy

## Scope

Sprint 34A provides local-only diagnostic evidence for unexpected application failures. It does not add telemetry, analytics, a cloud endpoint, automatic upload, or background transmission.

## Default state

Local diagnostics are disabled by default. A user must enable them in Settings and save the setting before PC-SPA creates an anonymous installation ID or stores crash events.

## Captured exception sources

When local diagnostics are enabled, PC-SPA records unexpected exceptions reaching:

- the WPF dispatcher
- the application domain unhandled-exception boundary
- the unobserved task-exception boundary
- main-window startup

Expected service failures that are already converted into safe result messages remain in their existing feature workflows.

## Local storage

Diagnostic evidence is stored under:

```text
%LOCALAPPDATA%\SystemPerformanceAccelerator\diagnostics\
```

The default retention policy is:

- maximum 50 local events
- maximum age 30 days
- oldest evidence removed first
- user-controlled deletion at any time

## Consent and export

PC-SPA never uploads diagnostics automatically.

A manual export requires:

1. local diagnostics to be enabled
2. an export preview
3. explicit confirmation
4. a user-selected ZIP destination

The user is instructed to inspect the ZIP before sharing it.

## Data minimization

Diagnostic records must not contain:

- file contents
- browser history
- passwords or cookies
- email addresses
- licence keys
- machine serial numbers
- unrelated process command lines
- full personal paths
- user account names as identity data

Known local paths are converted to tokens such as `%USERPROFILE%`, `%LOCALAPPDATA%`, `%TEMP%`, `%WINDIR%`, and `%APPDIR%`. Email-like values and remaining unknown absolute paths are redacted.

## Fatal versus recoverable events

- WPF dispatcher and AppDomain unhandled exceptions are recorded as fatal and the application closes.
- Unobserved task exceptions are recorded as recoverable and marked observed.
- Every record states whether PC-SPA recovered and whether user data may have been affected.

Sprint 34A records `UserDataMayHaveBeenAffected = false` at the global application boundary because the boundary cannot prove that a destructive operation was active. Future feature-specific evidence may refine this field only with explicit operation context.

## User controls

Settings provides:

- enable or disable local diagnostics
- include or exclude a basic hardware summary from manual exports
- open the diagnostics folder
- preview and export a diagnostic package
- copy the latest error reference
- delete diagnostic history
- reset the anonymous installation ID

Resetting the installation ID also deletes existing diagnostic history to prevent records from different anonymous identities being mixed.
