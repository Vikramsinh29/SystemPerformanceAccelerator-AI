# System Performance Accelerator

## Purpose

System Performance Accelerator is a safe, offline Windows 10/11 desktop utility for system cleaning and storage analysis. It uses a compact CCleaner-style WPF interface and keeps all processing local to the computer.

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

- Completely read-only startup inventory
- HKCU and HKLM `Run` key enumeration
- 64-bit and 32-bit registry views where applicable
- Current-user and all-users Startup folder enumeration
- Name, command/path, source, location, and status display
- Read-only StartupApproved status detection when available
- Missing, unresolved, and malformed target reporting
- Shortcut resolution without executing startup items
- Safe handling of inaccessible keys/folders, malformed values, and cancellation
- No enable, disable, delete, edit, execution, or registry-writing actions

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

### Auto Clean Schedule foundation

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
- Manual `Preview now` scans selected categories and reports files, reclaimable space, and issues
- Preview remains read-only and does not delete or modify files
- Maximum schedule count and malformed local-data handling fail safely
- Integrated with the central Sprint 17 feature-access system
- Planning foundation only: no automatic execution, Windows Task Scheduler, background service, registry change, or unattended cleanup

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

- Auto Clean Schedule uses the shared premium cards, buttons, text boxes, ComboBoxes, editor hierarchy, overview, and status presentation while remaining planning-only
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

## Verified state

- Release build succeeds.
- xUnit suite: 103 passed, 0 failed.
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
- Auto Clean Schedule overview, create/edit page, Back navigation, create, edit, remove, preview, cancel, save, enable/disable, and multiple-schedule behaviour remained functional and planning-only.
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
- The verified test baseline entering Sprint 25 is 103 passed, 0 failed; the final Sprint 25 Release build and full xUnit gate must pass before commit.
- No command, binding, converter, service, safety rule, cleanup rule, scan rule, scheduling rule, monitoring rule, settings rule, licensing rule, or system-modification behaviour changed.
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

Sprint 25 — Full Application Visual Consistency Audit.

## Next milestone

Sprint 26 — Full Functional Regression and Safety Audit, only after Sprint 25 is committed, pushed, and the working tree is clean.

## Compact AI handoff

Work in this repository. Read this README, inspect Git status and the exact current files, preserve the CCleaner-style WPF/MVVM architecture, and implement only the requested narrow sprint. For failures, follow the mandatory troubleshooting protocol: reproduce, inspect, identify the root cause, make one evidence-based change, clean-build, run all tests, verify every affected module, and stop before commit.
