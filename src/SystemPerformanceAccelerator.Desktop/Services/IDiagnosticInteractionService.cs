using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Desktop.Services;

public interface IDiagnosticInteractionService
{
    bool ConfirmExport(DiagnosticExportPreview preview);

    string? SelectExportPath(string suggestedFileName);

    bool ConfirmDeleteHistory(int eventCount);

    bool ConfirmResetInstallationId();

    void OpenFolder(string path);

    void CopyText(string value);
}
