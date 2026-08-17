# PC-SPA Commercial Launch Readiness Audit

Status: active launch-control document

Target release: PC-SPA 1.0.0 stable

Desktop baseline: `3982088d50fdc3d953d6201e007b8363fc289de4`

Commercial preparation branch: `release/commercial-1.0.0`

## Locked launch architecture

This document records the engineering consequences of the approved commercial launch strategy. Material changes require explicit product-owner approval.

- Microsoft Store/MSIX is the preferred primary consumer installation and update channel.
- `getpcspa.com` remains the primary marketing, pricing, account, subscription, licensing, support, documentation, and customer-relationship channel.
- PC-SPA-owned secure commerce is the intended commerce model where Microsoft Store policy permits it for this non-gaming product.
- Commercial Licensing V2 is authoritative for entitlement and device activation.
- The existing Inno Setup EXE pipeline is retained as a secondary/future direct-download channel and must not be represented as signed until it is actually signed.
- Store and direct packages must represent the same functional application version and source baseline.

## Release state already prepared

The commercial branch has already moved release packaging from Beta to stable `1.0.0` and removed the 30-day Beta startup expiry path.

Commercial release preparation is not equivalent to launch readiness. The blockers below remain authoritative.

## BLOCKER 1 — Whole-application administrator elevation

Current desktop manifest requests:

`requestedExecutionLevel level="requireAdministrator"`

This means the complete PC-SPA process runs elevated, including screens and read-only features that do not inherently require administrator privileges.

This is a Store/MSIX architecture blocker and a least-privilege concern.

Required direction:

1. Change the normal desktop process to non-elevated execution where technically possible.
2. Inventory every operation that genuinely requires administrator privileges.
3. Introduce a narrow privileged-operation boundary for those operations rather than elevating the entire UI process.
4. Preserve explicit user intent and Windows consent before privileged actions.
5. Keep read-only and current-user operations non-elevated whenever possible.
6. Add tests proving privileged commands cannot be invoked through arbitrary command or argument injection.

Do not simply remove `requireAdministrator` without implementing and testing the privileged-operation boundary.

## BLOCKER 2 — Windows Repair privilege model

Windows Repair currently executes approved DISM RestoreHealth and SFC ScanNow command shapes through explicit request types. This whitelist is a valuable safety boundary and must be retained.

Current approved operations include:

- `DISM.exe /Online /English /Cleanup-Image /RestoreHealth /NoRestart`
- `sfc.exe /scannow`

The current runner uses redirected standard output/error and runs the command directly in the application process context.

Required direction:

- Preserve exact command allowlisting.
- Move the privileged execution itself behind the future privileged-operation boundary.
- Do not accept arbitrary executable paths or argument lists from UI, website, licensing, update metadata, or remote input.
- Preserve normal completion behavior once a Microsoft repair process has started unless a separately reviewed cancellation design is introduced.

## BLOCKER 3 — Startup Manager privilege split

Startup Manager intentionally scans both current-user and all-users startup locations, including HKCU/HKLM Run keys, 32-bit/64-bit registry views, and current/all-users Startup folders.

The implementation already distinguishes access-denied conditions and notes that all-users changes may require administrator access.

Required direction:

- Keep inventory/read-only scans available without whole-app elevation where Windows permissions allow.
- Keep current-user state changes in the normal process when safe.
- Route all-users/HKLM protected state changes through the narrow privileged-operation boundary.
- Preserve the existing stale-state, identity, command, file-metadata, and post-write verification checks.
- Never broaden the feature into arbitrary registry editing.

## BLOCKER 4 — Microsoft Store/MSIX packaging does not yet exist

The repository currently has no Store/MSIX package project or `Package.appxmanifest` release pipeline. Existing public packaging is based on a portable publish plus Inno Setup EXE.

Required direction:

1. Add Store packaging as a separate build/distribution path.
2. Do not destroy or silently alter the existing direct installer pipeline.
3. Keep package identity/version mapping deterministic.
4. Verify WPF/.NET 10 compatibility under the chosen Store packaging model.
5. Test installation, uninstall, upgrade, settings persistence, local diagnostics, feedback, cleanup, startup management, Windows Repair, and licensing under package identity.
6. Add package validation and Store-specific test gates before submission.

## BLOCKER 5 — Commercial desktop authentication/authorization is incomplete

The production licensing client exists and supports:

- account licence lookup
- device activation
- device deactivation
- device validation

However, the desktop application does not currently construct and use that production client during normal startup/UI composition.

The client requires an authenticated bearer token. A browser-to-desktop authorization flow that safely gives the desktop the required credential is not yet implemented.

Required commercial flow:

customer account authorization -> one-time installation authorization -> desktop credential -> entitlement lookup -> device activation/validation -> local/offline entitlement -> feature access

Do not embed service secrets, signing secrets, privileged Cloudflare credentials, or reusable internal service tokens in the desktop application.

## BLOCKER 6 — Signed offline entitlement/runtime is incomplete

The Commercial Licensing V2 architecture requires a signed entitlement that can be cached for bounded offline use. The desktop should contain only the verification material required to validate that entitlement; it must never contain the entitlement-signing private secret.

Required direction:

- Define the signed entitlement payload/version.
- Define issuer/audience/product/device binding and validity fields.
- Define verification key rotation.
- Define secure local credential/entitlement storage.
- Define offline validity and warning behavior.
- Fail safely on malformed, expired, revoked, mismatched, or unverifiable entitlement state.
- Keep payment grace and offline outage tolerance as separate concepts.

## BLOCKER 7 — Free/Pro commercial entitlement boundary is not finalized

The current feature-access configuration defaults to `Free`, and the current entitlement table makes every existing application feature available to Free.

That is valid as a development foundation but is not a finalized commercial Free/Pro product definition.

Before commercial launch:

1. Explicitly approve which features are Free and which require Pro.
2. Encode that policy centrally in the entitlement catalogue.
3. Ensure navigation and executable commands are both protected by the same effective entitlement.
4. Ensure offline entitlement validation feeds the effective edition.
5. Add tests for Free, Trial if retained, Pro, expired, grace, revoked, offline-valid, and offline-expired states.

Do not infer the paid feature boundary ad hoc during UI implementation.

## BLOCKER 8 — Commercial UI still contains legacy Beta presentation in large XAML surfaces

The commercial branch has removed the Beta expiry runtime, but the large desktop XAML/ViewModel surfaces still require final conversion from Beta-oriented account presentation to commercial account/licence presentation.

Required direction:

- Replace `Beta Access` customer wording with approved commercial account/licence wording.
- Remove `OPEN BETA ACCESS`, `BETA ACCESS OPEN`, activation-free Beta messaging, Beta release/expiry presentation, and Beta-specific feedback labels from the commercial build.
- Do not display a fake active licence state while Commercial Licensing V2 is not connected.
- Until licensing is functional, present a clearly non-release `commercial licensing not connected` development state.

A release candidate must contain zero customer-visible Beta access/expiry language unless explicitly retained for historical release notes.

## BLOCKER 9 — Web distribution contract must remain Store-first and fail closed

Web V2 Phase 4 now models Microsoft Store as the preferred primary distribution channel and a signed direct installer as secondary fallback.

Required rules:

- No Store button until an official HTTPS Microsoft Store URL is configured.
- No direct commercial installer button unless URL is HTTPS, SHA-256 is valid, and signing is explicitly verified.
- Prefer Microsoft Store whenever both Store and direct channels are available.
- Never label an unsigned installer as signed.
- Never silently substitute one binary under an existing version.

## BLOCKER 10 — Update strategy must not conflict between Store and direct channels

For the Store package, Microsoft Store is the preferred update channel.

For a future signed direct installer, PC-SPA may need a separate controlled update path.

Required direction:

- Do not ship a custom updater that competes with Store-managed package updates.
- Detect/discriminate distribution channel before applying update behavior.
- Keep release metadata centralized.
- Ensure Store and direct packages use the same semantic product version and functional baseline.
- Never allow a Store install to self-replace itself with the direct EXE distribution.

## HIGH PRIORITY — README/documentation drift

The repository root README still contains historical Open Beta language, including activation-free operation and Beta expiry details. Treat those sections as historical baseline documentation, not the commercial release contract.

This audit plus the commercial launch strategy are authoritative for `release/commercial-1.0.0` until the root README is safely refactored in a dedicated documentation pass.

Do not copy historical Beta policy into new commercial code or UI merely because it remains in the root README.

## Security invariants for future development

- Fail closed for missing or unverifiable commercial entitlement.
- Never trust client-supplied price, plan, payment status, edition, or entitlement state.
- Never embed backend signing secrets or privileged service credentials in the desktop.
- Bind device activation to a privacy-conscious device identifier.
- Preserve explicit confirmation for destructive or system-changing operations.
- Preserve stale-state revalidation before file, registry, startup, or repair actions.
- Keep remote input unable to construct arbitrary local commands.
- Keep diagnostics user-reviewed and privacy-conscious.
- Separate distribution trust from commercial licensing trust.

## Required implementation order

1. Finish commercial-neutral desktop presentation and restore a clean Release build/test state.
2. Design and implement the least-privilege privileged-operation boundary.
3. Refactor whole-app elevation into operation-scoped elevation and validate all current modules.
4. Complete browser-to-desktop commercial authorization.
5. Complete device activation/validation and signed offline entitlement verification.
6. Approve and encode the Free/Pro feature boundary.
7. Add Store/MSIX packaging as a parallel packaging path.
8. Run Store-package functional compatibility tests for every module.
9. Complete Partner Center package/listing/capability/commerce declarations.
10. Certify the Store package.
11. Configure the official Store URL in Web V2 and verify the Store-first download page.
12. Keep direct-download commercial release disabled until independent signing is available and verified.

## Launch gate

PC-SPA 1.0.0 must not be called commercially launch-ready until all of the following are true:

- stable version metadata is consistent
- no Beta expiry runtime remains
- no customer-visible Beta access/expiry presentation remains
- whole-app mandatory elevation has been replaced or explicitly proven compatible with the approved Store model
- privileged operations are narrowly controlled and tested
- commercial browser authorization works end-to-end
- device entitlement/validation works end-to-end
- offline entitlement verification works end-to-end
- Free/Pro policy is explicitly approved and enforced
- Store/MSIX package builds reproducibly
- all automated tests pass
- complete functional Store-package testing passes
- Store certification requirements are satisfied
- privacy/support/listing information is complete
- website Store link points only to the official certified listing
- no production cutover occurs before independent preview/end-to-end verification

## Change control

This audit is intended to prevent later chats, branches, or release work from treating partial commercial preparation as launch readiness. A blocker may be removed only when implementation and verification evidence exist. Temporary inconvenience is not sufficient reason to weaken a launch gate.
