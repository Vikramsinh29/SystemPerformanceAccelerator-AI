# System Performance Accelerator

## Purpose
An offline-first Windows 10/11 desktop utility that safely identifies removable temporary files, previews them, requires explicit confirmation, performs cancellable cleanup, and reports before/after results.

## Users
Windows home users, power users, and IT technicians.

## Product principles
- Never delete user data without explicit confirmation.
- Preview every cleanup candidate before deletion.
- Use least privilege; Sprint 1 does not require administrator access.
- No telemetry or cloud account.
- Report partial failures honestly.

## Explicit non-goals
No registry hacks, driver updates, antivirus replacement, automatic startup modification, cloud dependency, or macOS/Linux/mobile support.

## Verified state
- **Planned/generated:** Sprint 1 source scaffold and unit-test project.
- **Unknown:** Build and runtime behaviour, because this generation environment has no .NET SDK or Windows WPF runtime.

## Architecture
- `Core`: platform-independent contracts and models.
- `Infrastructure`: Windows/local file-system implementation.
- `Desktop`: WPF MVVM user interface.
- `Tests`: xUnit tests for cleanup policy behaviour.

Dependencies point inward: Desktop → Infrastructure → Core.

## Canonical commands
```powershell
dotnet restore .\SystemPerformanceAccelerator.slnx
dotnet build .\SystemPerformanceAccelerator.slnx -c Release
dotnet test .\SystemPerformanceAccelerator.slnx -c Release --no-build
dotnet run --project .\src\SystemPerformanceAccelerator.Desktop\SystemPerformanceAccelerator.Desktop.csproj
```

## Security and data handling
Only files under the current user's temporary directory are scanned. Reparse points are ignored. Cleanup uses exact paths returned by the scan, checks they remain inside the approved root, and never follows directories outside it. Errors are reported per item.

## Known limitations
Sprint 1 scans only the current user's temporary directory. Startup optimization, duplicate detection, large-file analysis, performance baselines, localization, installer packaging, signing, and elevation workflows are not implemented.

## Current milestone
Sprint 1 — Safe temporary-file scan, preview, confirmed cleanup, cancellation, and results.

## Exact next milestone
Add measurable before/after storage and elapsed-time reporting with persisted privacy-safe local history.

## Compact AI handoff
Work in this repository. Read this README, Git status, repository instructions, and only files related to the requested milestone. Preserve offline-first and confirmation-before-deletion rules. Implement one bounded feature, run relevant build/tests, report actual results, and pause before commit.
