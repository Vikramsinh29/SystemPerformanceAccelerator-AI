# Sprint 34A — Quality and Diagnostic Foundation

## Objective

Add the minimum privacy-safe evidence system required to support a controlled PC-SPA beta without changing cleanup, monitoring, startup-management, scheduling, licensing, or recommendation behaviour.

## Starting point

- Product: PC-SPA 1.0.0
- Baseline commit: `b0eee1f45b0c637a8ff78dc8ede01979926227f6`
- Baseline automated tests reported by the user: 130 passed, 0 failed
- Architecture: .NET 10, WPF, MVVM, Core/Infrastructure/Desktop/Tests
- Distribution: Windows x64 self-contained portable ZIP

## Implemented scope

- opt-in local diagnostics, disabled by default
- anonymous random installation ID
- global WPF, AppDomain, task, and startup exception boundaries
- sanitized local crash-event JSON
- atomic writes
- 50-event and 30-day retention
- manual export preview and confirmation
- inspectable ZIP export
- optional CPU and memory summary
- open folder, copy reference, delete history, and reset ID controls
- build, runtime, Windows, and elevation display
- privacy, data dictionary, release, and sprint documentation
- unit and integration-style filesystem tests

## Explicit exclusions

- remote telemetry
- cloud crash reporting
- automatic upload
- user analytics
- performance benchmarking
- compatibility-result collection
- recommendation rule IDs
- false-positive dashboard
- cleanup-engine changes
- installer or code-signing changes
