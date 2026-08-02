using System.Runtime.InteropServices;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Desktop.Services;

internal sealed class DisabledDiagnosticService : IDiagnosticService
{
    public bool IsEnabled => false;

    public bool IncludeHardwareSummary => false;

    public string DiagnosticsRoot => string.Empty;

    public string? InstallationId => null;

    public string? LatestErrorReference => null;

    public DiagnosticEnvironment CurrentEnvironment { get; } = new(
        "1.0.0",
        "not-available",
        RuntimeInformation.OSDescription,
        RuntimeInformation.FrameworkDescription,
        false,
        null,
        null,
        null,
        null);

    public void Configure(
        bool enabled,
        bool includeHardwareSummary)
    {
    }

    public Task<string?> RecordExceptionAsync(
        Exception exception,
        string feature,
        string operationStage,
        bool recovered,
        bool userDataMayHaveBeenAffected,
        DiagnosticSeverity severity = DiagnosticSeverity.Error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Task.FromResult<string?>(null);
    }

    public DiagnosticExportPreview CreateExportPreview() =>
        new(
            0,
            [],
            false,
            string.Empty,
            "Diagnostics are unavailable in this non-production composition.");

    public Task<DiagnosticExportResult> ExportAsync(
        string destinationZipPath,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(
            new DiagnosticExportResult(
                false,
                destinationZipPath,
                0,
                "Diagnostics are unavailable in this non-production composition."));

    public Task<DiagnosticExportResult> ExportFeedbackAsync(
        string destinationZipPath,
        DiagnosticFeedbackRequest feedback,
        CancellationToken cancellationToken = default) =>
        ExportAsync(destinationZipPath, cancellationToken);

    public void DeleteHistory()
    {
    }

    public void ResetInstallationId()
    {
    }
}
