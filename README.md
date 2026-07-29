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
- xUnit suite: 35 passed, 0 failed.
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
- Large File Finder remained functional after Sprint 7; its pre-existing narrow-window Folder or Drive field compression is deferred to a dedicated responsive-layout sprint.
- System Monitor live CPU and physical-memory values, Start/Stop controls, automatic stop on tab change, and read-only scope manually verified.
- Cleaner, Large File Finder, Duplicate File Finder, and Startup Manager regression opening checks passed after Sprint 8.

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

Sprint 6 — Safe Duplicate Cleanup.

## Next milestone

Select one narrow functional module or reliability improvement only after Sprint 6 is committed and the working tree is clean.

## Compact AI handoff

Work in this repository. Read this README, inspect Git status and the exact current files, preserve the CCleaner-style WPF/MVVM architecture, and implement only the requested narrow sprint. For failures, follow the mandatory troubleshooting protocol: reproduce, inspect, identify the root cause, make one evidence-based change, clean-build, run all tests, verify every affected module, and stop before commit.
