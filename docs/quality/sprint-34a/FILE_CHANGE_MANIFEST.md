# Sprint 34A File Change Manifest

## Modified

```text
.gitignore
README.md
src/SystemPerformanceAccelerator.Core/Models/ApplicationSettings.cs
src/SystemPerformanceAccelerator.Infrastructure/Services/ApplicationSettingsService.cs
src/SystemPerformanceAccelerator.Desktop/App.xaml.cs
src/SystemPerformanceAccelerator.Desktop/MainWindow.xaml
src/SystemPerformanceAccelerator.Desktop/MainWindow.xaml.cs
src/SystemPerformanceAccelerator.Desktop/ViewModels/MainWindowViewModel.cs
src/SystemPerformanceAccelerator.Desktop/ViewModels/SettingsViewModel.cs
tests/SystemPerformanceAccelerator.Tests/ApplicationSettingsServiceTests.cs
```

## Added

```text
src/SystemPerformanceAccelerator.Core/Interfaces/IDiagnosticService.cs
src/SystemPerformanceAccelerator.Core/Models/DiagnosticEnvironment.cs
src/SystemPerformanceAccelerator.Core/Models/DiagnosticEvent.cs
src/SystemPerformanceAccelerator.Core/Models/DiagnosticExportPreview.cs
src/SystemPerformanceAccelerator.Core/Models/DiagnosticExportResult.cs
src/SystemPerformanceAccelerator.Core/Models/DiagnosticSeverity.cs

src/SystemPerformanceAccelerator.Infrastructure/Diagnostics/DiagnosticPathSanitizer.cs
src/SystemPerformanceAccelerator.Infrastructure/Diagnostics/InstallationIdentityService.cs
src/SystemPerformanceAccelerator.Infrastructure/Diagnostics/LocalDiagnosticService.cs
src/SystemPerformanceAccelerator.Infrastructure/Diagnostics/DiagnosticPackageExporter.cs

src/SystemPerformanceAccelerator.Desktop/Services/IDiagnosticInteractionService.cs
src/SystemPerformanceAccelerator.Desktop/Services/DiagnosticInteractionService.cs
src/SystemPerformanceAccelerator.Desktop/Services/DisabledDiagnosticService.cs
src/SystemPerformanceAccelerator.Desktop/Services/NonInteractiveDiagnosticInteractionService.cs

tests/SystemPerformanceAccelerator.Tests/DiagnosticPathSanitizerTests.cs
tests/SystemPerformanceAccelerator.Tests/InstallationIdentityServiceTests.cs
tests/SystemPerformanceAccelerator.Tests/LocalDiagnosticServiceTests.cs
tests/SystemPerformanceAccelerator.Tests/DiagnosticPackageExporterTests.cs

docs/quality/CRASH_REPORTING_POLICY.md
docs/quality/DIAGNOSTIC_DATA_DICTIONARY.md
docs/quality/PRIVACY_REVIEW_CHECKLIST.md
docs/quality/RELEASE_CERTIFICATION_CHECKLIST.md
docs/quality/sprint-34a/SPRINT_QUALITY_EVIDENCE.md
docs/quality/sprint-34a/IMPLEMENTATION_SUMMARY.md
docs/quality/sprint-34a/FILE_CHANGE_MANIFEST.md
docs/quality/sprint-34a/TEST_RESULTS.md
docs/quality/sprint-34a/KNOWN_LIMITATIONS.md
docs/quality/sprint-34a/NEXT_SPRINT_RECOMMENDATIONS.md
```

No cleanup service, startup service, health-check rule, schedule engine, entitlement rule, or release-signing configuration is modified.
