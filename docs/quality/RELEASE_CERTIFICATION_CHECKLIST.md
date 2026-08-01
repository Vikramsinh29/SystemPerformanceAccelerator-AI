# PC-SPA Release Certification Checklist

A release candidate must not be published until all applicable items are completed.

## Source state

- [ ] Correct branch and intended commit confirmed.
- [ ] Working tree clean before release packaging.
- [ ] `git diff --check` passes.
- [ ] Release version and product metadata are correct.

## Automated verification

- [ ] Clean Release build succeeds.
- [ ] Existing tests remain green.
- [ ] New Sprint 34A diagnostic tests pass.
- [ ] No compiler warnings or errors.
- [ ] Portable publish verification succeeds.
- [ ] ZIP and SHA-256 files are generated.

## Core safety regression

- [ ] Cleaner requires reviewed selection and confirmation.
- [ ] Custom Clean requires preview, selection, and confirmation.
- [ ] Large File Finder uses the Recycle Bin and revalidates files.
- [ ] Duplicate Finder leaves at least one verified copy.
- [ ] Startup Manager changes state only after confirmation.
- [ ] Auto Clean Schedule remains manual-only.
- [ ] No destructive operation bypasses confirmation.
- [ ] Cancellation leaves each module consistent.

## Diagnostic verification

- [ ] Diagnostics are disabled on a clean settings profile.
- [ ] Enabling and saving diagnostics creates one anonymous ID.
- [ ] No event is stored while diagnostics are disabled.
- [ ] A controlled test exception produces a sanitized local record.
- [ ] The latest error reference can be copied.
- [ ] Export preview accurately reports event count.
- [ ] Export ZIP can be opened and inspected.
- [ ] Optional hardware summary follows the saved setting.
- [ ] Delete history removes event files.
- [ ] Reset ID deletes history and produces a new random ID.
- [ ] No network request occurs during capture or export.

## UI and compatibility smoke tests

- [ ] Settings diagnostics card works in Light theme.
- [ ] Settings diagnostics card works in Dark theme.
- [ ] Normal, maximized, and resized layouts remain usable.
- [ ] 100%, 125%, 150%, and 200% display scaling checked where practical.
- [ ] Windows 10 x64 smoke test completed.
- [ ] Windows 11 x64 smoke test completed or explicitly marked blocked.
- [ ] Non-English or Unicode path smoke test completed.

## Release blockers

Do not approve the release if any of these are true:

- a known critical data-loss defect exists
- a high-risk cleanup false positive remains unresolved
- a core workflow crashes
- a supported Windows version lacks required smoke testing
- logs or exports contain unsanitized personal paths
- diagnostic data is transmitted without explicit architecture and consent
- destructive operations bypass confirmation
- automated tests fail

## Sprint 35A Windows Repair Assessment

- [ ] Only DISM CheckHealth and SFC VerifyOnly can be generated.
- [ ] No repair, CHKDSK, restart, registry, or component-cleanup command exists.
- [ ] Microsoft processes are not force-terminated.
- [ ] Stop after current check skips remaining checks.
- [ ] Unknown or localized output is Inconclusive.
- [ ] Exported reports contain sanitized evidence only.
- [ ] Windows 10 physical smoke test completed.
- [ ] Windows 11 physical smoke test completed or marked blocked.

## Sprint 35B1 guided-repair safety preview

- [ ] Healthy assessments show `Repair is not recommended`.
- [ ] Attention assessments require a fresh, complete, non-blocked preflight.
- [ ] Inconclusive, failed, unsupported, skipped, stale, and future-dated
      assessments fail closed.
- [ ] Pending-restart detection is read-only and blocks planning when detected
      or unavailable.
- [ ] The preview visibly states that it is not consent and cannot execute a
      repair.
- [ ] No repair action, automatic restart, CHKDSK, registry repair, component
      cleanup, or background download is available.
- [ ] Deleting Windows Repair history also removes saved repair-plan previews.

## Sprint 35B2 combined guided repair

- [ ] Only an eligible fresh Attention assessment can reach repair confirmation.
- [ ] Fresh elevation, tool, restart, and free-space checks run at execution time.
- [ ] Confirmation names DISM RestoreHealth, SFC Scannow, verification, Windows
      Update possibility, no forced termination, and no automatic restart.
- [ ] DISM uses only `/Online /English /Cleanup-Image /RestoreHealth /NoRestart`.
- [ ] SFC repair uses only `/scannow`.
- [ ] DISM failure stops before SFC and verification.
- [ ] Stop after current step skips only commands that have not started.
- [ ] Read-only DISM CheckHealth and SFC VerifyOnly verification run after repair.
- [ ] Healthy is never claimed from repair exit codes alone.
- [ ] Repair-execution history is sanitized, local, and retention bounded.
- [ ] CHKDSK, registry repair, component cleanup, custom sources, scheduling,
      background repair, and automatic restart remain unavailable.
