using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface IDiagnosticService
{
    bool IsEnabled { get; }

    bool IncludeHardwareSummary { get; }

    string DiagnosticsRoot { get; }

    string? InstallationId { get; }

    string? LatestErrorReference { get; }

    DiagnosticEnvironment CurrentEnvironment { get; }

    void Configure(
        bool enabled,
        bool includeHardwareSummary);

    Task<string?> RecordExceptionAsync(
        Exception exception,
        string feature,
        string operationStage,
        bool recovered,
        bool userDataMayHaveBeenAffected,
        DiagnosticSeverity severity = DiagnosticSeverity.Error,
        CancellationToken cancellationToken = default);

    DiagnosticExportPreview CreateExportPreview();

    Task<DiagnosticExportResult> ExportAsync(
        string destinationZipPath,
        CancellationToken cancellationToken = default);

    void DeleteHistory();

    void ResetInstallationId();
}
