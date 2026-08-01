# Sprint 35B1 - Guided Repair Safety Plan

## Purpose

Sprint 35B1 adds a read-only readiness preview for a possible future guided
Windows repair. It does not execute, schedule, authorize, or simulate a repair.

## Evidence gate

A plan is considered for future review only when:

- the latest assessment is no more than 24 hours old under PC-SPA policy
- the assessment contains an `Attention` result rather than `Healthy`,
  `Inconclusive`, `Failed`, `Unsupported`, or `Skipped`
- the current session is Windows and elevated
- the required Microsoft DISM and SFC executables are present
- supported pending-restart markers are not detected
- Windows-drive free space can be read
- assessment and runtime preflight records contain no unresolved issue

A healthy assessment produces `Repair is not recommended`. Unknown or failed
evidence fails closed.

## Previewed sequence

The preview describes, but cannot execute:

1. repeat preflight and request fresh consent
2. repair the Windows component store
3. repair protected Windows files
4. run fresh read-only verification

The preview discloses that Microsoft component servicing may use Windows
Update. It never claims that a repair source is available.

## Consent and restart boundaries

- A saved preview is not consent.
- A future execution must repeat every safety check.
- A future execution must present a new explicit confirmation.
- No automatic restart is proposed.
- CHKDSK, registry repair, component cleanup, and reboot scheduling remain
  outside this sprint.

## Persistence

Sanitized plan previews are stored locally under the existing Windows Repair
assessment area. Retention is bounded to 20 records and 90 days. Deleting
Windows Repair history also removes saved previews.
