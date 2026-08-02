# PC-SPA — System Performance Accelerator

## Purpose

PC-SPA (System Performance Accelerator) is a safe, offline Windows 10/11 desktop utility for system cleaning and storage analysis. It uses a compact CCleaner-style WPF interface and keeps all processing local to the computer.

## Product direction

- Professional CCleaner-style desktop utility; never use a ChatGPT-style interface.
- Compact, sharp, practical controls with clear system status and actions.
- Preserve the existing WPF/MVVM architecture.
- Work through narrow, separately verified sprints.
- No cloud APIs, telemetry, or external account requirement.
- Never delete user data without explicit confirmation.
- Report partial failures and skipped files honestly.

## Technology

- Windows 10/11
- .NET 10
- C#
- WPF
- MVVM
- xUnit
- Offline only

## Solution structure

- `src/SystemPerformanceAccelerator.Core`
- `src/SystemPerformanceAccelerator.Infrastructure`
- `src/SystemPerformanceAccelerator.Desktop`
- `tests/SystemPerformanceAccelerator.Tests`
- Solution: `SystemPerformanceAccelerator.slnx`

Dependencies point inward: Desktop → Infrastructure → Core.

## Current implemented features

### Cleaner

- Temporary-file scanning
- Results preview and file selection
- Confirmation before cleanup
- Cancellable cleanup
- Locked, unavailable, read-only, and inaccessible file handling
- Deleted, skipped, and reclaimed-space reporting

### Custom Clean

- Uses the existing Cleaner temporary-file scanning and safe cleanup services
- Existing Cleaner category selection only; no new cleanup rules
- Temporary Files selected by default
- Category, filename, size, modified date, and location display
- Read-only preview before cleanup
- Cleanup remains disabled until a successful preview exists
- Explicit confirmation before processing previewed items
- Selecting No leaves all previewed files unchanged
- Selecting Yes processes only the previewed items from supported selected categories
- Safe cancellation during preview and cleanup
- Locked, unavailable, missing, unsafe, and failed files are skipped and reported honestly
- Deleted, skipped, failed, reclaimed-space, and duration reporting
- No registry cleanup or unrelated system-changing action

- Premium overview hero explains category selection, read-only preview, and explicit confirmation
- Premium `Preview selected`, `Clean previewed`, and `Cancel current operation` actions use the shared design system
- Cleanup Category, Categories Selected, Files Found, and Reclaimable Space cards use consistent hierarchy, spacing, icons, and status colours
- Premium empty state appears when the current preview has no results
- Preview table uses the shared Fluent table design while preserving category, filename, size, modified date, and location
- Preview state, progress, status, operation result, and shared bottom status-panel presentation remain visible and consistent
- Light and dark themes and normal, maximized, and resized layouts are supported
- Existing Custom Clean categories, services, commands, confirmation flow, cancellation, safety checks, and cleanup scope are unchanged

### Large File Finder

- Folder or drive selection
- Configurable minimum file size
- Recursive read-only scanning
- Progress, cancellation, and inaccessible-folder handling
- Results sorted by size
- Selection checkboxes
- Confirmed movement of selected files to the Windows Recycle Bin
- Protection for Windows, Program Files, ProgramData, application, recovery, boot, reparse-point, system, and out-of-scope paths
- Deleted/skipped/reclaimed totals

### Duplicate File Finder

- Folder or drive selection
- Recursive read-only scanning
- Files grouped by size before hashing
- SHA-256 hashing only for same-sized candidate files
- Duplicate groups confirmed by matching file content, never by filename
- Progress, cancellation, and honest skipped-item reporting
- Safe handling of inaccessible folders, locked files, reparse points, scan errors, and files changed during scanning
- Duplicate-group, confirmed-file, potential-reclaimable-space, selected-file, and selected-size summaries
- Manual selection only; nothing is selected automatically
- At least one verified copy must remain in every duplicate group
- Confirmation before cleanup
- Selected duplicate copies move to the Windows Recycle Bin
- Size, SHA-256 hash, modified time, scan scope, and path safety are revalidated before cleanup
- Locked, missing, changed, inaccessible, unsafe, outside-scope, and reparse-point files are skipped and reported
- Deleted, skipped, and reclaimed-space totals
- Results refresh after cleanup
- Duplicate files displayed in clearly separated content-matched groups
- Group number, confirmed copy count, per-file size, and potential reclaimable space shown in each group header
- Select, File Name, Size, Modified, and Location columns aligned consistently
- Light row and column separators improve report readability
- Individual selection remains available within every group
- Structured completion statistics with truncated status text and a full-message tooltip

### Startup Manager

- HKCU and HKLM `Run` key enumeration
- 64-bit and 32-bit registry views where applicable
- Current-user and all-users Startup folder enumeration
- Name, command/path, source, location, target condition, and startup state display
- Windows `StartupApproved` state detection and verified state changes
- Compact row-level status controls: green Enabled, muted red Disabled, amber Unknown, and blue Updating
- Click an Enabled or Disabled status to change only that startup item
- Explicit confirmation before every state change
- Original registry commands and Startup-folder files are never deleted or rewritten
- Fresh identity, command, file metadata, and target checks before each state change
- Inventory refresh and state verification after successful changes
- Current-user and all-users state support with 32-bit and 64-bit registry handling
- Missing, unresolved, malformed, stale, unsupported, and access-denied reporting
- Shortcut resolution without executing startup items
- Safe handling of inaccessible keys/folders, malformed values, cancellation, and partial scan failures
- Total, Enabled, Disabled, and Unknown counters always account for every displayed row
- The desktop application requests administrator permission at launch for protected Windows startup locations
- No startup entry deletion, command editing, startup-item execution, or unrelated registry modification

### System Monitor

- Completely read-only live system monitoring
- Total CPU usage sampled from Windows system processor times
- Physical memory used, available, total, and usage percentage
- Approximately one refresh per second
- Explicit Start and Stop controls
- Monitoring stops automatically when leaving the System Monitor tab
- Safe cancellation and Windows API failure handling
- No process termination, memory optimization, cleanup, disk/network monitoring, or system-setting changes

### Health Check

- Completely read-only on-demand system health summary
- System-drive free-space assessment
- Current CPU and physical-memory assessment using the existing monitor service
- Startup-item summary using the existing startup service
- Clear `Good`, `Attention`, and `Unknown` statuses
- Findings are shown first without opening recommendations automatically
- Every finding provides its own `View recommendations` action
- Opening a recommendation replaces the findings list with a focused detail page
- Recommendation detail shows priority, recommended change, why it matters, safety warning, and available manual action
- `Back to Health Findings` restores the findings list
- Context-specific navigation links open Cleaner, Large File Finder, Duplicate Finder, System Monitor, or Startup Manager
- Recommendations remain guidance only; no generic Apply, Fix, Disable, Delete, or Optimize action exists
- Safe cancellation and honest partial-failure reporting
- No cleanup, repair, optimization, process termination, service modification, registry writing, or automatic system-setting changes

- Premium overview hero clearly communicates read-only scope and user-requested recommendations
- Premium `Run health check` and `Cancel current check` actions use the shared design system
- Overall, Good, Attention, and Unknown summary cards use consistent hierarchy, icons, spacing, and colour semantics
- Premium empty state appears before the first health check
- Findings table uses the shared Fluent table design with clearer recommendation access
- Focused recommendation page presents current status, current value, detected condition, recommendation, reason, warning, and available action in a structured layout
- Premium Back navigation and context-specific action buttons remain explicit and user-controlled
- Light and dark themes, normal and maximized layouts, and shared bottom status-panel standards are supported
- Existing Health Check services, rules, thresholds, commands, navigation targets, and safety behaviour are unchanged

### Cross-tool result presentation

- Consistent wrapped status panels across Cleaner, Custom Clean, Large File Finder, Duplicate File Finder, Startup Manager, System Monitor, and Health Check
- Structured cleanup summaries for Cleaner, Custom Clean, Large File Finder, and Duplicate File Finder
- Deleted or recycled, skipped, failed, reclaimed-space, and duration values shown separately
- First issue displayed in a dedicated readable warning area
- Long completion and error messages wrap cleanly
- Presentation-only implementation with no scanning, cleanup, safety-rule, or business-logic changes

### Settings

- Local-only settings stored as JSON under `%LOCALAPPDATA%\SystemPerformanceAccelerator\settings.json`
- System, Light, and Dark appearance choices
- Default Large File Finder minimum size
- Configurable System Monitor refresh interval from 1 to 10 seconds
- Save Settings and Restore Defaults actions
- Values persist safely across application restarts
- Invalid, missing, or malformed settings fall back safely to defaults
- Cleanup confirmation remains permanently enabled as a non-disableable safety rule
- No cloud account, telemetry, registry writing, startup modification, or automatic optimization

### Edition and feature access foundation

- One shared codebase supports Trial, Free, Standard, Pro, and Business editions
- Explicit edition hierarchy keeps Trial separate from the commercial Free-to-Business ranking
- Strongly typed catalogue covers every current application module
- One central entitlement configuration defines feature availability
- Central feature-access service returns Available, Trial, Locked, or Hidden states
- Access results include the required edition and a user-facing availability message
- Unknown editions, features, requirements, and malformed entitlement entries fail closed
- Reusable access guard protects both module navigation and executable commands
- Sidebar badges and locked-feature presentation are reusable without redesigning module layouts
- Debug-only local override uses the `SPA_DEVELOPMENT_EDITION` environment variable
- Release builds ignore the local development override
- The normal Sprint 17 configuration keeps every existing module available
- Trial, Locked, and Hidden behaviour is exercised without adding billing or real licensing
- No payment gateway, subscription, account, product key, hardware fingerprint, licence server, or permanent commercial assignment

### Auto Clean Schedule

- Local schedule plans are stored under `%LOCALAPPDATA%\SystemPerformanceAccelerator\auto-clean-schedules.json`
- Create separate Daily, Weekly, and Monthly schedule plans
- Configure local run time, weekday, or monthly day as appropriate
- Include only existing safety-reviewed Cleaner categories
- Enable and disable individual schedule plans
- Calculate and display the next planned local run
- Dedicated Fluent-inspired schedule overview and create/edit pages
- Existing schedules remain visible and are not replaced when a new schedule is created
- Editing updates only the explicitly selected schedule
- Multiple distinct schedules persist safely across application restarts
- Manual `Preview now` in the editor scans selected categories and reports files, reclaimable space, and issues without changing files
- `Run now` is available for one explicitly selected saved schedule, including a disabled schedule
- Every manual run performs a completely fresh scan from the schedule's saved categories
- Fresh run previews begin with no files selected and show individual file details and scan issues
- Cleanup starts only after the user selects reviewed files and accepts an explicit confirmation
- Cleanup reuses the existing Custom Clean service, including stale-file revalidation, progress, cancellation, and honest partial-failure reporting
- Latest completed manual-run totals are stored locally with the schedule and remain backward compatible with older schedule JSON
- Maximum schedule count and malformed local-data handling fail safely
- Integrated with the central Sprint 17 feature-access system
- Manual execution only: no automatic execution, Windows Task Scheduler, background service, registry change, or unattended cleanup

### Premium visual design system

- Central Fluent-inspired design tokens for light and dark themes
- Unified application palette, typography, spacing, corner radius, and elevation
- Reusable premium card and elevated-card styles
- Reusable primary, secondary, danger, and navigation button styles
- Modern text box, combo box, checkbox, progress bar, and table styles
- Consistent status badges, alert panels, page headers, and active navigation states
- Shared shell styling applied without changing feature or service behaviour
- ThemeManager updates all mutable design brushes consistently
- Existing module layouts remain functional and responsive
- No feature logic, cleanup behaviour, licensing rule, or system-modification behaviour changed

### Cleaner premium UI migration

- Cleaner uses the shared premium Fluent-inspired design system
- Premium action hero explains manual review and confirmation requirements
- Clear `Scan now`, `Clean selected`, and `Cancel current operation` actions
- Summary cards show files found, reclaimable space, and current activity
- Cleanup candidates remain individually reviewable before confirmation
- Improved results table, empty state, progress, and operation-result presentation
- Responsive layout works at normal and maximized window sizes
- Light and dark themes remain fully supported
- Existing Cleaner commands, cleanup categories, confirmation flow, and safety behaviour are unchanged
- No new cleanup rules, automatic cleaning, scheduling behaviour, or deletion logic was added

### Premium window chrome and shell polish

- Custom theme-aware title bar replaces the disconnected native light strip
- Premium minimize, maximize/restore, and close caption controls
- Native window dragging, double-click maximize/restore, resize borders, Windows Snap, and system menu remain available
- Maximized windows respect the Windows taskbar and restore correctly
- Duplicate title-bar branding was removed; the primary brand remains in the sidebar
- Title-bar, sidebar, and page-header junctions use aligned shell measurements
- Light and dark themes update the title bar, sidebar, branding, navigation, footer, cards, tables, controls, and module surfaces immediately
- Light mode uses a fully light, readable sidebar palette instead of retaining the dark sidebar
- Shared module margins improve right-panel alignment across all screens
- All principal bottom status panels use one shared minimum height, spacing, badge placement, and icon geometry
- Shared status icon containers use consistent 36 × 36 dimensions; colour varies only by information, success, warning, or danger meaning
- Settings ComboBox and dropdown use the shared Fluent control template in both themes
- No feature, service, command, cleanup, scheduling, licensing, or system-modification behaviour changed

### Remaining modules premium UI migration

- Auto Clean Schedule uses the shared premium cards, buttons, text boxes, ComboBoxes, editor hierarchy, overview, fresh-run review table, and status presentation while remaining manual-only
- Large File Finder uses a premium safety hero, summary cards, scan controls, results table, selection actions, and shared status presentation
- Duplicate File Finder uses a premium hash-confirmed safety hero, duplicate summaries, folder controls, grouped results, recycle actions, and shared status presentation
- Startup Manager uses a premium read-only inventory hero, summary cards, scan controls, results table, and shared status presentation
- System Monitor uses a premium read-only telemetry hero, CPU and memory summaries, live metric cards, Start/Stop controls, and shared status presentation
- Settings uses a premium local-settings hero, shared cards, Fluent inputs and ComboBoxes, theme preview, Restore Defaults, and Save Settings actions
- All six modules support Light and Dark themes and normal, maximized, and resized layouts
- Existing commands, bindings, table schemas, scheduling plans, scan rules, cleanup safeguards, monitoring behaviour, settings persistence, and feature-access rules are unchanged
- No automatic cleanup execution, Windows Task Scheduler integration, background service, startup-item modification, process termination, registry modification, or new system-changing behaviour was added

### Full application visual consistency

- Shared module hero styles standardize 52 × 52 hero icons, title typography, description typography, padding, and spacing
- Shared summary-metric styles standardize 40 × 40 icon containers, glyph alignment, and 24 px primary metric values
- Auto Clean Schedule card geometry is aligned with the shared premium card radius
- Large File Finder, Duplicate File Finder, and Startup Manager use the shared Fluent DataGrid presentation
- Premium visual-only empty states appear before those three result tables contain data
- Schedule, scan, cleanup, monitoring, Restore Defaults, Save Settings, and related actions use consistent existing MDL2 icons
- Button icon placement, disabled-state readability, table presentation, status-panel geometry, and summary-card hierarchy were checked across the application
- Existing commands, view-model bindings, converters, table schemas, services, safety rules, and module behaviour are unchanged
- No new feature, cleanup rule, scan rule, scheduling rule, monitoring rule, settings rule, licensing rule, or system-changing behaviour was added

### Functional regression and cleanup safety audit

- Cleaner revalidates current file size and last-modified time against the reviewed scan result immediately before permanent deletion
- Cleaner skips files changed after scanning and requires a fresh scan before deletion
- Large File Finder revalidates current file size and last-modified time against the reviewed scan result immediately before Recycle Bin cleanup
- Large File Finder skips files changed after scanning and requires a fresh scan before recycling
- Large File Finder rejects a scanned root that becomes a reparse point before cleanup
- Large File Finder rejects candidate paths containing parent-directory reparse points
- Shared DataGrid row and cell backgrounds remain theme-aware, preventing light table bodies with unreadable light text in Dark mode
- Cleaner and Large File Finder stale-result protection were manually verified with controlled files, including successful processing only after a fresh scan
- Custom Clean, Duplicate File Finder, Health Check, Auto Clean Schedule, Startup Manager, System Monitor, Settings, feature access, confirmation, cancellation, persistence, and read-only boundaries were regression verified
- Auto Clean Schedule remains manual-only with explicit preview and confirmation; System Monitor remains read-only
- No background service, automatic cleanup, registry writing, startup modification, process termination, payment, subscription, cloud service, or unrelated system-changing behaviour was added

### Responsive layout

- Large File Finder Folder or Drive field remains usable at restored and narrow widths
- Folder/path and minimum-size fields resize proportionally
- Crowded action buttons use dedicated rows instead of compressing input fields
- Wide result tables expose horizontal scrolling when required
- Settings expands with the available window width
- Header content avoids overlap during resizing
- Sidebar respects practical minimum and maximum widths
- Restored, maximized, narrow, and stretched layouts are supported
- UI-only implementation with no command, binding, service, scanning, cleanup, or settings-logic changes

### Shared WPF table interaction

All selectable result tables must use the same application-wide behaviour:

- One click on a checkbox toggles it immediately.
- One click on a row highlights it.
- Double-clicking a row toggles its checkbox.
- Interactive controls inside a row must not cause accidental row toggling.
- Cleaner, Large File Finder, and Duplicate File Finder must behave identically.
- Shared behaviour belongs in a reusable WPF behaviour/style, not duplicated per grid.

### Tri-state bulk selection and responsive review layout

- Cleaner, Custom Clean, Large File Finder, and Duplicate File Finder expose a consistent `ALL` tri-state header control.
- Checked means every safely selectable item is selected, unchecked means none are selected, and indeterminate means the selection is partial.
- Custom Clean preview items begin selected to preserve the established preview-and-clean workflow.
- Large File Finder scan results remain initially unselected.
- Duplicate Finder bulk selection targets only removable copies and always leaves at least one verified copy in every duplicate group.
- Manual row selection, confirmation, cancellation, path validation, stale-result protection, Recycle Bin behaviour, and final-copy protection remain unchanged.
- Light-mode selected navigation remains readable.
- Custom Clean uses compact wrapped summary cards, a readable responsive preview table, a left-aligned hero description, and no duplicate category checkbox.
- Custom maximization remains constrained to the Windows work area above the taskbar.

### Windows x64 portable release

- Application version `1.0.0` is defined through explicit assembly, file, product, and informational-version metadata.
- The supported first distribution is a portable Windows 10/11 x64 ZIP.
- Publishing is self-contained, so the extracted application carries its required .NET runtime files.
- Single-file publishing, trimming, ReadyToRun, installer creation, automatic updating, and code signing are intentionally excluded from this release.
- `Windows-x64-Portable.pubxml` defines the repeatable Release publish configuration.
- `scripts/Publish-Windows-x64.ps1` performs the clean build, full tests, self-contained publish, metadata checks, PDB removal, ZIP creation, SHA-256 generation, archive validation, and signature-status reporting.
- Generated publish and release artifacts remain under the ignored `artifacts` directory.
- The final manually verified portable ZIP has SHA-256 `2390cee212f559d9fd7cdf3d3a6cb8589dba968b905ee5e5c84db94fc348ab18`.
- The unsigned executable is expected to report `NotSigned`; Windows may therefore show an Unknown Publisher or SmartScreen warning.

### Windows x64 installer foundation

- `installer/PC-SPA.iss` defines a standalone Inno Setup installer for the verified self-contained Windows x64 publish.
- Installation is per-machine under `Program Files`, requests administrator permission, creates a Start Menu shortcut, and offers a desktop-shortcut option that is selected by default.
- Silent installation and uninstallation are supported. Silent installation never launches PC-SPA automatically.
- Installation and uninstallation do not automatically restart Windows.
- User-created settings, diagnostics, and history under local application data are not installer-managed and remain available across upgrades and uninstall unless the user deletes them separately.
- `scripts/Publish-Windows-x64-Installer.ps1` reruns the verified portable release pipeline before compiling the installer, then generates a SHA-256 file and reports installer and published-PE signature status.
- Inno Setup 6 must already be installed, or `INNO_SETUP_COMPILER` must identify `ISCC.exe`; the packaging script never downloads build tools.
- Code signing remains required before public or Microsoft Store distribution.

### Windows code-signing readiness

- `scripts/Test-Windows-CodeSigningReadiness.ps1` performs a read-only signing-readiness audit and never signs files, imports certificates, downloads tools, or stores private-key passwords.
- The audit locates the newest available x64 Windows SDK SignTool, or uses the explicit `PCSPA_SIGNTOOL_PATH` override.
- Signing identity is selected by a 40-character certificate thumbprint from the current-user or local-machine personal certificate store; certificate files and private keys are never stored in the repository.
- Certificate readiness requires an accessible private key, current validity, and the Code Signing enhanced-key-usage identifier.
- Timestamp readiness requires an explicit absolute HTTP or HTTPS URL through `PCSPA_SIGNING_TIMESTAMP_URL`.
- The audit reports signature state for the four PC-SPA-owned published PE files and the Windows installer without modifying Microsoft runtime files.
- `-RequireReady` fails closed when any signing prerequisite or expected artifact is missing.
- Actual signing and timestamping remain excluded until an approved code-signing certificate is available.

### Controlled beta distribution

- `scripts/Publish-Controlled-Beta.ps1` reruns the verified installer pipeline and creates a local controlled-beta ZIP without publishing it externally.
- The bundle contains the installer, its SHA-256 file, controlled-beta installation and security instructions, and a focused tester feedback checklist.
- The publisher independently verifies the installer hash before bundling and validates every required ZIP entry afterward.
- Instructions disclose administrator permission, unsigned-publisher and SmartScreen warnings, offline operation, no telemetry, no automatic restart, and retained local data.
- Testers are instructed not to fabricate Windows Repair evidence, force an unhealthy assessment, or delete personal files merely for testing.
- The bundle and its SHA-256 file are copied to the Desktop for distribution only to invited beta testers.
- GitHub releases, public uploads, automatic updates, certificate acquisition, and actual code signing remain outside this controlled-beta packaging sprint.

## Verified state

- Release build succeeds.
- xUnit suite: 111 passed, 0 failed.
- Cleaner scan and safe cleanup manually verified.
- Large File Finder scan and Recycle Bin cleanup manually verified.
- Shared one-click checkbox and double-click row selection manually verified in all selectable tables.
- Duplicate File Finder content-confirmed scanning manually verified.
- Duplicate cleanup manual selection, confirmation, final-copy protection, Recycle Bin movement, reclaimed-space reporting, and result refresh manually verified.
- Locked/in-use duplicate files are skipped without crashing or removing the remaining copy.
- Duplicate Finder status layout, full-message tooltip, and maximize/restore behaviour manually verified.
- Cleaner and Large File Finder regression opening checks passed after Sprint 6.
- Startup Manager registry and Startup-folder enumeration manually verified.
- Startup Manager re-scan, cancellation, status reporting, restored/maximized layout, and read-only controls manually verified.
- Cleaner and Duplicate File Finder regression opening checks passed after Sprint 7.
- Large File Finder Folder or Drive compression was resolved and manually verified after Sprint 14.
- Restored, maximized, narrow, and stretched layouts were manually verified across every implemented module.
- Action rows, table scrolling, Settings width, sidebar sizing, and header overlap behaviour were manually verified after Sprint 14.
- Cleaner, Custom Clean, Large File Finder, Duplicate File Finder, Startup Manager, System Monitor, Health Check, and Settings remained functionally unchanged during Sprint 14 regression verification.
- Duplicate Finder grouping, group headers, copy counts, reclaimable-space summaries, aligned columns, separators, scrolling, and row selection manually verified after Sprint 15.
- Duplicate Finder final-copy protection and safe Recycle Bin cleanup remained unchanged and were regression verified after Sprint 15.
- Release build succeeded and the xUnit suite remained at 52 passed, 0 failed after Sprint 15.
- Health Check findings-first flow, per-area View recommendations actions, focused recommendation pages, and Back navigation manually verified after Sprint 16.
- Recommendation priority, recommended change, why-it-matters explanation, warning/safety guidance, and context-specific tool navigation manually verified after Sprint 16.
- Disk recommendations correctly navigate to Cleaner, Large File Finder, and Duplicate Finder; CPU and memory recommendations navigate to System Monitor; startup recommendations navigate to Startup Manager.
- No generic Apply, Fix, Disable, Delete, Optimize, or automatic system-changing action is present in Health Check.
- Release build succeeded and the xUnit suite increased to 55 passed, 0 failed after Sprint 16.
- Cleaner, Custom Clean, Large File Finder, Duplicate File Finder, Startup Manager, System Monitor, and Settings regression checks passed after Sprint 16.
- Edition hierarchy, feature availability, Trial, Locked, Hidden, development override, malformed configuration, and unknown-value behaviour are covered by automated tests.
- Release mode manually verified as `Free edition • local system utility` with every existing module available and no unexpected access badges.
- Debug Trial override manually verified as `Trial edition • local development override` with visible TRIAL badges and every existing module remaining available.
- Cleaner, Health Check, Custom Clean, Large File Finder, Duplicate File Finder, Startup Manager, System Monitor, and Settings navigation and commands were regression verified after Sprint 17.
- Health Check recommendation navigation continued to open the correct feature-access-guarded target modules.
- Release build succeeded and the xUnit suite increased to 89 passed, 0 failed after Sprint 17.
- No payment, subscription, online licensing, account, product-key, hardware-fingerprint, or permanent commercial-tier implementation was added.
- Auto Clean Schedule launch, sidebar navigation, summary cards, overview page, dedicated create/edit page, and Back navigation manually verified after Sprint 18.
- Daily, Weekly, and Monthly schedule creation, next-run calculation, enable/disable, explicit selected-schedule editing, and local persistence across restart manually verified.
- Creating additional schedules was verified to preserve every previously saved schedule instead of replacing one.
- Manual Preview now was verified to report files, reclaimable space, and issues without deleting or modifying files.
- Cleaner and Custom Clean regression checks passed after Sprint 18.
- The read-only PreviewProgress binding was explicitly constrained to OneWay, preventing the verified WPF startup crash.
- Release build succeeded and the xUnit suite increased to 103 passed, 0 failed after Sprint 18.
- No automatic cleanup execution, Windows Task Scheduler integration, background service, registry modification, or unattended deletion was added.
- Premium Fluent-inspired shell, navigation, typography, cards, buttons, inputs, checkboxes, progress bars, tables, badges, alerts, and page headers were manually verified after Sprint 19.
- Cleaner, Health Check, Custom Clean, Auto Clean Schedule, Large File Finder, Duplicate File Finder, Startup Manager, System Monitor, and Settings were all visually and functionally regression checked.
- Light and dark themes were manually verified for readable text, visible control states, active navigation, table selection, and consistent colour treatment.
- Shared controls were verified without clipping, overlap, broken commands, or responsiveness regressions.
- Release build succeeded and the xUnit suite remained at 103 passed, 0 failed after Sprint 19.
- No feature logic, cleanup behaviour, licensing rule, automatic execution, or system-modification behaviour changed.
- Cleaner premium action hero, safety messaging, summary cards, cleanup-candidate table, empty state, progress, and result presentation were manually verified after Sprint 20.
- Cleaner `Scan now`, `Clean selected`, `Cancel current operation`, confirmation, and result-summary behaviour were regression verified.
- Cleaner was manually verified in light and dark themes and at normal and maximized window sizes.
- Health Check, Custom Clean, Auto Clean Schedule, Large File Finder, Duplicate File Finder, Startup Manager, System Monitor, and Settings remained functional and theme-aware in their current phased-migration layouts.
- No Cleaner service, view-model, command, cleanup category, confirmation rule, scheduling behaviour, or deletion logic changed.
- Release build succeeded and the xUnit suite remained at 103 passed, 0 failed after Sprint 20.
- Custom title bar, minimize, maximize/restore, close, dragging, double-click maximize/restore, resizing, Windows Snap, system menu, and taskbar-safe maximized behaviour were manually verified after Sprint 21.
- Duplicate title-bar branding and the unnecessary empty title-bar divider were removed.
- Sidebar branding and the page-header separator were aligned to the same 80 px shell measurement.
- Light and dark title bar, sidebar, branding, navigation, footer, module surfaces, cards, tables, controls, disabled states, and status areas were manually verified.
- Switching themes while the application remained open updated every current screen without retaining stale theme colours.
- Cleaner, Health Check, Custom Clean, Auto Clean Schedule, Large File Finder, Duplicate File Finder, Startup Manager, System Monitor, and Settings use standardized right-panel margins and bottom status-panel geometry.
- Shared bottom status panels and 36 × 36 status icon containers were visually verified for consistent size, spacing, colour semantics, and alignment.
- Settings theme selection and dropdown presentation were verified in Light and Dark modes.
- All existing module navigation, commands, scans, previews, monitoring, cleanup safeguards, schedules, settings, and recommendation navigation remained functional.
- Release build succeeded and the xUnit suite remained at 103 passed, 0 failed after Sprint 21.
- No feature, service, command, cleanup, scheduling, licensing, or system-modification behaviour changed.
- Health Check premium overview hero, read-only safety message, `Run health check` and `Cancel current check` actions, summary cards, empty state, findings table, and shared bottom status panel were manually verified after Sprint 22.
- Overall, Good, Attention, and Unknown summary values and status colour semantics were regression verified.
- Findings-first behaviour remained intact: recommendations do not open automatically and each finding retains its own `View recommendation` action.
- Focused recommendation details, Back navigation, priority, current status, current value, detected condition, recommendation, reason, warning, and available action were manually verified.
- Context-specific navigation to Cleaner, Large File Finder, Duplicate File Finder, System Monitor, and Startup Manager continued to open the correct modules.
- Health Check was manually verified in Light and Dark themes and at normal, maximized, and resized window sizes without clipping or overlap.
- Cleaner, Custom Clean, Auto Clean Schedule, Large File Finder, Duplicate File Finder, Startup Manager, System Monitor, and Settings remained functionally unchanged.
- Release build succeeded and the xUnit suite remained at 103 passed, 0 failed after Sprint 22.
- No Health Check service, view-model, command, threshold, recommendation rule, system-modification, or automatic-fix behaviour changed.
- Custom Clean premium overview hero, preview-first safety message, `Preview selected`, `Clean previewed`, and `Cancel current operation` actions were manually verified after Sprint 23.
- Cleanup category selection and busy-state disabling remained functional.
- Categories Selected, Files Found, Reclaimable Space, preview state, progress, status, and operation-result values were regression verified.
- Premium empty state and preview-results table were manually verified with category, filename, size, modified date, and location intact.
- Existing confirmation-No, confirmation-Yes, cancellation, supported-category scope, and safe cleanup behaviour remained unchanged.
- Shared bottom status-panel size, icon geometry, colour semantics, and alignment matched the other premium modules.
- Custom Clean was manually verified in Light and Dark themes and at normal, maximized, and resized window sizes without clipping, overlap, or misalignment.
- Cleaner, Health Check, Auto Clean Schedule, Large File Finder, Duplicate File Finder, Startup Manager, System Monitor, and Settings remained functionally unchanged.
- Release build succeeded and the xUnit suite remained at 103 passed, 0 failed after Sprint 23.
- No Custom Clean service, view-model, command, cleanup category, cleanup rule, confirmation, safety, or system-modification behaviour changed.
- The combined Sprint 24 premium UI migration was manually verified across Auto Clean Schedule, Large File Finder, Duplicate File Finder, Startup Manager, System Monitor, and Settings.
- Auto Clean Schedule overview, create/edit page, Back navigation, create, edit, remove, preview, cancel, save, enable/disable, and multiple-schedule behaviour remained functional; later manual Run now execution still requires a fresh review and confirmation.
- Large File Finder folder selection, minimum-size input, scan, cancellation, result columns, selection, confirmation, and safe Recycle Bin cleanup behaviour remained functional.
- Duplicate File Finder folder selection, scan, grouping, selection summary, result columns, cancellation, final-copy protection, confirmation, and safe Recycle Bin cleanup behaviour remained functional.
- Startup Manager scan, cancellation, totals, five-column inventory, and strictly read-only behaviour remained functional; no disable, delete, edit, or registry-writing action was added.
- System Monitor Start, Stop, live CPU, physical-memory values, progress indicators, memory details, tab-leave cancellation, and read-only behaviour remained functional.
- Settings theme selection, immediate Light/Dark updates, Large File Finder default, monitor refresh interval, mandatory confirmation, Restore Defaults, Save Settings, persistence, and local settings path remained functional.
- All six modules were manually verified in Light and Dark themes and at normal, maximized, and resized window sizes, including shared status-panel dimensions, icon alignment, dropdowns, disabled controls, tables, and action layouts.
- Exact pre-existing command and binding sets were preserved for every migrated module, and the Large File Finder, Duplicate File Finder, and Startup Manager table schemas remained unchanged.
- Release build succeeded and the xUnit suite remained at 103 passed, 0 failed after Sprint 24.
- No feature, service, command, scheduling rule, scan rule, cleanup rule, monitoring rule, settings-persistence rule, licensing rule, or system-modification behaviour changed.
- The complete Sprint 25 visual-consistency matrix was manually verified across Cleaner, Health Check, Custom Clean, Auto Clean Schedule, Large File Finder, Duplicate File Finder, Startup Manager, System Monitor, and Settings.
- Module hero icon dimensions, title typography, description typography, padding, and alignment were verified in Light and Dark themes.
- Summary-card icon dimensions, glyph alignment, primary metric typography, card geometry, and colour semantics were verified across modules.
- Large File Finder, Duplicate File Finder, and Startup Manager shared DataGrid presentation, five-column schemas, empty states, and populated result states were manually verified.
- Action-button icons, text spacing, disabled states, dropdowns, tables, status panels, and module navigation were verified at normal, maximized, and resized window sizes.
- Auto Clean overview/editor actions, large-file scan and safe deletion, duplicate scan/grouping/recycling, Startup Manager read-only scope, System Monitor Start/Stop, and Settings Restore Defaults/Save remained functional.
- All 47 existing command bindings and all 336 existing view-model bindings were preserved; three ElementName bindings were added only for visual empty-state presentation.
- Release build succeeded and the xUnit suite remained at 103 passed, 0 failed after Sprint 25.
- No command, binding, converter, service, safety rule, cleanup rule, scan rule, scheduling rule, monitoring rule, settings rule, licensing rule, or system-modification behaviour changed.
- Sprint 26 completed a full functional regression and safety audit across Cleaner, Health Check, Custom Clean, Auto Clean Schedule, Large File Finder, Duplicate File Finder, Startup Manager, System Monitor, Settings, and feature-access behaviour.
- Cleaner and Large File Finder now skip stale scan results when current size or last-modified time differs from the reviewed candidate and require a fresh scan before cleanup.
- Controlled manual tests verified that changed Cleaner and Large File Finder files remained untouched, reported the stale-result condition honestly, and were processed only after a fresh scan.
- Large File Finder scanned-root and parent-directory reparse-point protections are covered by focused automated regression tests.
- The shared DataGrid style now sets a theme-aware row background and transparent cell background; Cleaner and Large File Finder table bodies were manually verified readable in both Dark and Light modes.
- Confirmation-No, confirmation-Yes, cancellation, empty, success, warning, failure, and partial-result paths were regression checked where applicable.
- Duplicate Finder retained its content verification, confirmation, Recycle Bin cleanup, and at-least-one-copy protection.
- Auto Clean Schedule remains manual-only with no unattended execution, Windows Task Scheduler integration, or background service.
- Startup Manager and System Monitor remained strictly read-only with no startup modification, registry writing, process termination, or optimization action.
- Settings theme switching, Save, Restore Defaults, restart persistence, mandatory cleanup confirmation, and local-only storage remained functional.
- Release build succeeded and the xUnit suite increased to 107 passed, 0 failed after Sprint 26.
- Sprint 26 changed only the two cleanup services, their focused tests, the shared Fluent DataGrid style, and this README.
- Sprint 27 added reusable tri-state Select All / Deselect All behaviour to Cleaner, Custom Clean, Large File Finder, and Duplicate File Finder.
- Checked, unchecked, partial-selection, refresh/reset, and Duplicate Finder retain-one-copy behaviour are covered by focused automated tests.
- Cleaner, Custom Clean, Large File Finder, and Duplicate File Finder bulk selection, manual selection, confirmation, cancellation, and protected-copy behaviour were manually verified.
- Custom Clean compact summary cards, wrapped text, duplicate-checkbox removal, preview-table visibility, `ALL` header readability, hero alignment, and maximized taskbar-safe layout were manually verified.
- Health Check retained the same compact summary-card presentation without clipping or unnecessary vertical expansion.
- Light-mode selected navigation remained clearly readable across modules.
- Release build succeeded and the xUnit suite increased to 111 passed, 0 failed after Sprint 27.
- Sprint 27 introduced no new scan rule, cleanup rule, background task, automatic cleanup, registry modification, process termination, cloud service, or unrelated system-changing behaviour.
- Sprint 28 added explicit `1.0.0` release metadata, a self-contained Windows x64 publish profile, and a reusable portable-release script.
- Clean Release build and all 111 tests passed before the release candidate was packaged.
- Self-contained `win-x64` publishing succeeded and included the required local .NET runtime without requiring a separately installed runtime.
- Publish-time PDB files were removed and the final archive was verified to contain no PDB files.
- The portable ZIP and its SHA-256 checksum were generated and independently verified before extraction.
- File version `1.0.0.0` and product version `1.0.0` with source-commit metadata were verified on the published executable.
- The extracted portable application launched successfully from a separate Desktop test folder.
- Cleaner, Health Check, Custom Clean, Auto Clean Schedule, Large File Finder, Duplicate File Finder, Startup Manager, System Monitor, and Settings opened correctly from the portable package.
- Light and Dark themes, settings persistence after restart, maximize/minimize/restore, tables, checkboxes, tri-state `ALL` selectors, normal shutdown, and missing-runtime/missing-DLL absence were manually verified.
- Large File Finder action order was standardized to `Scan files`, `Cancel`, then the destructive `Delete selected` action at the far right.
- Duplicate Finder retained the consistent `Scan duplicates`, `Cancel`, then `Recycle selected` action order.
- Auto Clean Schedule retained `Edit selected`, then `Remove`, and the destructive Remove action now uses the shared danger-button treatment.
- The corrected action order, danger styling, alignment, normal-window layout, and maximized layout were manually verified before the portable release was regenerated.
- The regenerated portable archive SHA-256 `2390cee212f559d9fd7cdf3d3a6cb8589dba968b905ee5e5c84db94fc348ab18` was verified before extraction and final launch testing.
- Startup Manager remained intentionally read-only for version `1.0.0`; safe startup enable/disable functionality is deferred to a separate post-release sprint.
- The executable remained intentionally unsigned, matching the confirmed no-code-signing Sprint 28 scope.
- Sprint 28 added no installer, updater, cloud service, telemetry, licensing, cleanup rule, scan rule, background execution, registry modification, process termination, startup-entry modification, or unrelated application feature.
- System Monitor live CPU and physical-memory values, Start/Stop controls, automatic stop on tab change, and read-only scope manually verified.
- Cleaner, Large File Finder, Duplicate File Finder, and Startup Manager regression opening checks passed after Sprint 8.
- Health Check system-drive, CPU, memory, and startup summary results manually verified.
- Health Check `Good`, `Attention`, and `Unknown` status presentation, cancellation, and read-only scope manually verified.
- Cleaner, Large File Finder, Duplicate File Finder, Startup Manager, and System Monitor regression opening checks passed after Sprint 9.
- Custom Clean category selection, read-only preview, cancellation, summaries, and no-delete scope manually verified.
- Custom Clean preview results were verified against the existing Cleaner temporary-file scan.
- Cleaner, Large File Finder, Duplicate File Finder, Startup Manager, System Monitor, and Health Check regression opening checks passed after Sprint 10.
- Custom Clean confirmation-No, confirmation-Yes, preview-only scope, safe execution, result totals, cancellation, and locked/unavailable-file handling manually verified.
- Custom Clean cleanup was verified to process only previewed items from the supported selected category through the existing safe Cleaner cleanup service.
- Cleaner, Large File Finder, Duplicate File Finder, Startup Manager, System Monitor, and Health Check regression checks passed after Sprint 11.
- Cross-tool structured result panels, summary values, first-issue presentation, message wrapping, and consistent status styling manually verified after Sprint 12.
- Cleaner, Custom Clean, Large File Finder, Duplicate File Finder, Startup Manager, System Monitor, and Health Check remained functionally unchanged during Sprint 12 regression verification.
- Settings System, Light, and Dark appearance choices manually verified.
- Settings persistence across restart, Save Settings, Restore Defaults, invalid-value handling, Large File Finder default size, and System Monitor refresh interval manually verified.
- Cleanup confirmation was verified to remain permanently enabled.
- Cleaner, Custom Clean, Large File Finder, Duplicate File Finder, Startup Manager, System Monitor, and Health Check regression checks passed after Sprint 13.

## Canonical commands

```powershell
cd "C:\Users\vikra\SystemPerformanceAccelerator-AI"

dotnet restore .\SystemPerformanceAccelerator.slnx
dotnet build .\SystemPerformanceAccelerator.slnx -c Release
dotnet test .\SystemPerformanceAccelerator.slnx -c Release --no-build

dotnet run --project `
  .\src\SystemPerformanceAccelerator.Desktop\SystemPerformanceAccelerator.Desktop.csproj `
  -c Release
```

## Mandatory development workflow

1. Inspect the actual current repository before editing.
2. Check `git status`, the current commit, and the exact affected files.
3. Define one narrow sprint objective and the minimum required file set.
4. Preserve existing commands, bindings, services, architecture, and verified behaviour.
5. Provide complete replacement files only when code changes are required.
6. Build in Release.
7. Run all tests.
8. Perform the required manual verification.
9. Stop before commit and review `git diff`.
10. Commit only after build, tests, and manual verification all pass.

## Mandatory troubleshooting protocol

This procedure exists to prevent repeated trial-and-error patches, wasted time, unnecessary downloads, stale executable testing, and token/resource waste.

### 1. Capture the real current state first

Before proposing any fix:

```powershell
cd "C:\Users\vikra\SystemPerformanceAccelerator-AI"

git status
git log -1 --oneline
dotnet build .\SystemPerformanceAccelerator.slnx -c Release
```

Inspect the exact files currently present on the user's machine. Do not assume that a previously generated ZIP or earlier response matches the current working tree.

For UI problems, inspect at minimum:

- `App.xaml`
- `MainWindow.xaml`
- `MainWindow.xaml.cs`
- Relevant view-models
- Shared styles and behaviours
- Bound row/item view-models

When the current files are unavailable, request one diagnostic ZIP containing the exact affected project. Do not guess from an older repository snapshot.

### 2. Reproduce and define the failure precisely

Write the expected and actual behaviour before editing. Example:

```text
Expected: a checkbox toggles on the first click.
Actual: the first click activates the DataGrid cell; the second click toggles it.
Affected scope: Cleaner and Large File Finder.
```

Determine whether the issue is:

- One control
- One table
- Every table
- A shared application-wide behaviour
- A view-model/binding issue
- A WPF routed-event or visual-tree issue

A requirement that applies across the application must be solved once at the shared layer.

### 3. No repeated speculative patches

- Do not issue multiple variations of the same unverified fix.
- Only one evidence-based patch may be attempted from the inspected source.
- If that patch fails, stop and collect the latest affected files and exact build/runtime output.
- Do not continue with v2, v3, v4-style guesses without new evidence.
- Explain the identified root cause before providing the next replacement.

### 4. WPF/DataGrid diagnostic checklist

For WPF interaction problems, inspect all of the following before changing code:

- `DataGrid.SelectionUnit` and `SelectionMode`
- Column type: `DataGridCheckBoxColumn` versus `DataGridTemplateColumn`
- Binding mode and `UpdateSourceTrigger`
- Whether the row item implements `INotifyPropertyChanged`
- Whether commands recalculate `CanExecute` after selection changes
- Routed-event direction: bubbling versus tunnelling
- Whether child controls mark mouse events as handled
- Row and cell virtualization
- Existing global styles, row styles, and event setters
- Code-behind handlers left behind by earlier patches
- Whether the running application is the newly compiled binary

Important WPF rules learned from this project:

- A standard `DataGridCheckBoxColumn` can require one click to enter edit mode and another to toggle. For immediate toggling, use a template column with a directly bound `CheckBox` or a verified shared behaviour.
- DataGrid cells and child controls may consume mouse events. Shared behaviours may need `AddHandler(..., handledEventsToo: true)`.
- Do not attach separate fragile handlers to every grid when the expected behaviour is application-wide.
- Row virtualization must be considered; behaviour must work for recycled/generated rows.
- Shared selection logic should target a common contract such as `ISelectableItem` rather than concrete row types.

### 5. Build gate: never test a stale executable

After every code change:

```powershell
cd "C:\Users\vikra\SystemPerformanceAccelerator-AI"

Get-Process SystemPerformanceAccelerator.Desktop -ErrorAction SilentlyContinue |
    Stop-Process -Force

dotnet clean .\SystemPerformanceAccelerator.slnx -c Release
dotnet build .\SystemPerformanceAccelerator.slnx -c Release
```

If the build fails:

- Stop immediately.
- Do not run tests with `--no-build` as proof of the new change.
- Do not launch the application.
- Do not evaluate UI behaviour using an older executable.
- Fix only the reported compiler/XAML error, then rebuild.

Run tests only after a successful build:

```powershell
dotnet test .\SystemPerformanceAccelerator.slnx -c Release --no-build
```

Launch only the verified Release project:

```powershell
dotnet run --project `
  .\src\SystemPerformanceAccelerator.Desktop\SystemPerformanceAccelerator.Desktop.csproj `
  -c Release
```

### 6. Use a verification matrix

For shared table-selection changes, verify every row below before commit:

| Behaviour | Cleaner | Large File Finder | Duplicate File Finder |
|---|---:|---:|---:|
| Single click highlights row | Required | Required | Required |
| First checkbox click toggles | Required | Required | Required |
| Double-click row toggles selection | Required | Required | Required |
| Double-click again unchecks | Required | Required | Required |
| Action button updates immediately | Required | Required | Required |
| Scan still works | Required | Required | Required |
| Cleanup still works safely | Required | Required | Required |
| Final copy remains protected | N/A | N/A | Required |

A fix is incomplete when it works in only one affected table.

### 7. Packaging and repository safety

- Prefer a changed-files ZIP for an existing Git repository.
- Preserve the `.git` directory and commit history.
- Do not copy ZIP files into the repository root.
- Exclude `bin`, `obj`, `.vs`, and temporary diagnostic files.
- After applying files, run `git status` and confirm only intended source files changed.
- Compare the actual changed-file list with the declared sprint scope.

### 8. Failure escalation rule

When the same issue remains after one evidence-based attempt:

1. Stop patching.
2. Request the latest exact affected files or diagnostic ZIP.
3. Inspect current XAML, code-behind, styles, behaviours, bindings, and build output.
4. Identify and state the root cause.
5. Replace the smallest correct file set.
6. Clean, build, test, and verify the full affected matrix.

This rule is mandatory. Repeated blind patches are not acceptable.

## Safety and data handling

- Temporary cleanup validates every path against its approved root.
- Large-file cleanup only acts on files returned from the selected scan scope.
- Reparse points and protected paths are rejected.
- Large files are moved to the Windows Recycle Bin instead of permanently deleted.
- Duplicate cleanup only acts on manually selected, content-confirmed files returned from the selected scan scope.
- Duplicate cleanup independently revalidates size, SHA-256 hash, modified time, scope, and path safety before recycling.
- At least one verified copy must remain in every duplicate group.
- Missing, locked, changed, inaccessible, read-only, unsafe, and protected files are skipped and reported.
- No cloud services or telemetry are used.

## Development rules for future AI sessions

- Read this README before making changes.
- Inspect the actual repository state; do not rely only on conversation history.
- One sprint equals one focused objective, one build, one complete test run, and manual verification.
- Do not redesign unrelated architecture.
- Do not add speculative modules.
- Keep responses concise and command-oriented.
- Use PowerShell commands.
- Never claim success when a build, test, or manual check was not actually completed.
- Pause before providing a Git commit command.

## Current milestone

Sprint 28 — Windows Release Readiness.

- Version `1.0.0` metadata, the self-contained `win-x64` publish profile, and the repeatable portable-release script are complete.
- Clean Release build, all 111 tests, self-contained publishing, ZIP/checksum generation, archive validation, and extracted portable-launch verification passed.
- Destructive-action placement is consistent across Large File Finder, Duplicate Finder, and Auto Clean Schedule, with destructive actions isolated at the far right and using danger styling where applicable.
- The final regenerated portable ZIP checksum is `2390cee212f559d9fd7cdf3d3a6cb8589dba968b905ee5e5c84db94fc348ab18`.
- The full portable manual matrix passed with no missing runtime, missing DLL, theme, persistence, navigation, window, table, selection, action-order, or shutdown regression.
- Startup Manager remains read-only in version `1.0.0`; safe enable/disable support is reserved for a later focused sprint.

## Next milestone

Sprint 29 — Version 1.0.0 Release Publication, only after Sprint 28 is committed, pushed, and the working tree is clean.

- Regenerate the final portable ZIP and SHA-256 checksum from the committed Sprint 28 source state.
- Verify final version metadata, source-commit metadata, archive contents, checksum, and one extracted launch smoke test.
- Publish the portable ZIP and checksum through the approved distribution channel without adding an installer, code signing, updater, telemetry, licensing, or unrelated features.

## Compact AI handoff

Work in this repository. Read this README, inspect Git status and the exact current files, preserve the CCleaner-style WPF/MVVM architecture, and implement only the requested narrow sprint. For failures, follow the mandatory troubleshooting protocol: reproduce, inspect, identify the root cause, make one evidence-based change, clean-build, run all tests, verify every affected module, and stop before commit.

## Sprint 33A - Premium design system foundation

Sprint 33A establishes a reusable commercial-quality visual foundation while preserving the existing CCleaner-style WPF/MVVM application structure and all feature behaviour.

- Central premium Light and Dark palettes use matte black, carbon black, gunmetal, warm light surfaces, metallic gold, rich gold hover states, premium white, and neutral gray.
- Green, amber, and red remain reserved for success, warning, and danger semantics.
- Shared design tokens now define typography, spacing, corner radii, padding, elevation, and restrained gold glow effects.
- Reusable resource dictionaries cover typography, cards, buttons, inputs, tables, progress indicators, scrollbars, icons, and window chrome.
- Existing Fluent style keys remain compatible so current modules continue to resolve their styles while later sprints migrate layouts incrementally.
- Primary actions use a metallic gold treatment; secondary and danger actions retain clear visual hierarchy and disabled states.
- Text boxes, combo boxes, checkboxes, radio buttons, and switch foundations share one premium control language.
- Data grids, progress indicators, tooltips, title-bar controls, navigation states, and scrollbars use the same commercial visual system.
- Sidebar selection uses a restrained gold surface, readable selected text, and a clear selection rail.
- Light and Dark theme switching continues through the local ThemeManager without cloud services, telemetry, or external dependencies.
- No Cleaner, Custom Clean, Large File Finder, Duplicate Finder, Startup Manager, System Monitor, Health Check, Auto Clean, safety, command, service, or persistence behaviour is changed.
- Final PC-SPA phoenix assets, executable renaming, splash branding, module-by-module layout migration, and launch asset packaging remain outside this foundation sprint.


## Sprint 33B - PC-SPA shell branding and commercial identity

Sprint 33B integrates the approved PC-SPA phoenix identity into the premium design-system foundation without changing feature behaviour.

- The supplied compact phoenix and PC-SPA wordmark are treated as the authoritative visual references.
- High-resolution theme-aware assets preserve the approved proportions, outer ring, circuit details, metallic gold treatment, and wordmark shape.
- The custom title bar now displays the PC-SPA phoenix and product name.
- The sidebar header replaces the temporary SPA tile with a larger phoenix and a clear PC-SPA / System Performance / Accelerator hierarchy.
- Settings includes a commercial About PC-SPA card with product identity, application version, supported Windows family, offline-first status, safety positioning, and repository reference.
- Product metadata and the Windows application icon use PC-SPA branding while internal namespaces, project names, architecture, and release-output names remain unchanged.
- ThemeManager switches between dark-optimized and light-optimized gold logo assets so Light theme does not show a black logo background.
- Images use WPF high-quality scaling and preserve aspect ratio; important descriptive text remains real WPF text for DPI clarity.
- Cleaner, Health Check, Custom Clean, Auto Clean, Large File Finder, Duplicate Finder, Startup Manager, System Monitor, Settings persistence, feature access, cleanup safety, and all service logic remain unchanged.
- Splash-screen branding, final launch asset master pack, executable/package renaming, multi-architecture publishing, and installer work remain for Sprint 33C.

## Sprint 33C - Launch identity, splash screen, and portable package naming

Sprint 33C completes the PC-SPA launch identity using the already approved brand assets without changing feature behaviour.

- The desktop assembly now publishes as `PC-SPA.exe` while the existing project names, namespaces, architecture, and repository structure remain unchanged.
- Windows executable metadata, UAC identity, taskbar identity, and Explorer file details use the PC-SPA product name and approved multi-resolution icon.
- Application startup now shows a borderless PC-SPA splash window using the approved full lockup at its original proportions and high-quality WPF scaling.
- The splash has no fake progress indicator and no artificial delay; it closes as soon as the verified main window is ready.
- The Windows x64 portable publish script now creates `PC-SPA-1.0.0-win-x64-portable.zip` and validates `PC-SPA.exe`, its runtime configuration, dependency manifest, and runtime files.
- Portable release notes instruct users to launch `PC-SPA.exe` and retain the existing offline, unsigned, administrator-elevation, and no-telemetry disclosures.
- Cleaner, Health Check, Custom Clean, Auto Clean, Large File Finder, Duplicate Finder, Startup Manager, System Monitor, Settings, safety rules, services, commands, persistence, and feature access remain unchanged.
- The repository contains approved raster launch assets and a multi-resolution ICO. A true editable vector/8K master must come from original design artwork and is not fabricated by this sprint.

## Sprint 34A - Quality and Diagnostic Foundation

Sprint 34A adds an opt-in, local-only diagnostic evidence foundation for controlled beta support without changing feature behaviour.

- Local diagnostics are disabled by default and require an explicit saved Settings choice.
- Unexpected WPF dispatcher, AppDomain, background-task, and startup exceptions can be recorded locally when diagnostics are enabled.
- Diagnostic records use a random anonymous installation ID and contain sanitized messages, sanitized stack traces, version/build context, Windows/runtime information, elevation state, and limited resource context.
- Personal paths, email-like values, document contents, browser history, credentials, cookies, licence keys, machine serial numbers, and unrelated process command lines are excluded by design.
- Local evidence is bounded to 50 events and 30 days.
- Settings provides Open diagnostics folder, Preview & export package, Copy error reference, Delete diagnostic history, and Reset installation ID controls.
- Diagnostic ZIP export is manual, previewed, user-confirmed, user-selected, and never uploaded automatically.
- Optional CPU and memory summary inclusion is controlled by the user.
- Cleaner, Custom Clean, Auto Clean Schedule, Large File Finder, Duplicate File Finder, Startup Manager, System Monitor, Health Check, edition access, safety confirmation, and release packaging behaviour remain unchanged.
- Remote telemetry, analytics, cloud crash reporting, benchmarking, compatibility claims, and false-positive scoring remain outside this sprint.

## Sprint 35A - Read-only Windows Repair Assessment

Sprint 35A adds a feature-access-controlled Windows Repair module that runs
only Microsoft DISM CheckHealth and SFC VerifyOnly assessment commands.

- Commands are started directly without Command Prompt, PowerShell, scripts,
  or user-supplied arguments.
- No RestoreHealth, ScanHealth, SFC Scannow, CHKDSK, component cleanup,
  registry repair, restart scheduling, or downloaded repair source is
  implemented.
- Stop after current check never force-terminates a running Microsoft process.
- Result interpretation is conservative; unknown or localized wording is
  Inconclusive rather than guessed.
- Sanitized local history is bounded to 20 records and 90 days.
- Reports are exported manually, contain no personal files, and are never
  uploaded automatically.
- Existing cleanup, startup, health, diagnostics, settings, edition, and
  packaging behaviour remains unchanged.

## Sprint 35B1 - Guided Repair Safety Plan

Sprint 35B1 adds a read-only guided-repair readiness preview without adding any
repair execution capability.

- A preview is created only from the latest saved Windows Repair assessment.
- PC-SPA applies a 24-hour assessment-freshness policy for repair planning.
- Healthy evidence produces `Repair is not recommended`.
- Inconclusive, failed, unsupported, skipped, stale, future-dated, or
  issue-bearing evidence fails closed.
- Current Windows, elevation, Microsoft-tool availability, pending-restart
  state, and readable Windows-drive free-space evidence are reviewed.
- The preview explains a possible future component-store repair,
  protected-file repair, and read-only verification sequence.
- Microsoft servicing may use Windows Update; PC-SPA does not claim that a
  repair source is available.
- A preview never authorizes repair and always requires fresh execution-time
  preflight and explicit consent.
- No repair command, process execution, automatic restart, CHKDSK, registry
  repair, component cleanup, or download is added.
- Sanitized preview records are local and bounded to 20 records and 90 days.

## Sprint 35B2 - Combined Guided Windows Repair

Sprint 35B2 adds one explicitly confirmed foreground repair chain for eligible
Attention assessments.

- Fresh execution-time safety checks repeat immediately before confirmation.
- PC-SPA requires at least 5 GB free on the Windows drive under product policy.
- The fixed repair commands are DISM RestoreHealth with NoRestart followed by
  SFC Scannow.
- DISM may use Windows Update; no custom source or LimitAccess policy is added.
- The existing read-only DISM CheckHealth and SFC VerifyOnly assessment runs
  again after both repair commands.
- Exit code 0 alone is not treated as proof of health; verification evidence
  determines the final result.
- Stop after current step never force-terminates an active Microsoft process.
- No automatic restart, CHKDSK, registry repair, component cleanup, scheduling,
  background repair, command shell, script, or user-supplied argument is added.
- Sanitized repair-execution history is local and bounded to 20 records and
  90 days.
