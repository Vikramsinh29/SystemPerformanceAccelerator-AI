using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Desktop.Services;

internal sealed class NonInteractiveDiagnosticInteractionService :
    IDiagnosticInteractionService
{
    public bool ConfirmExport(DiagnosticExportPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return false;
    }

    public string? SelectExportPath(string suggestedFileName) =>
        null;

    public bool ConfirmDeleteHistory(int eventCount) =>
        false;

    public bool ConfirmResetInstallationId() =>
        false;

    public void OpenFolder(string path)
    {
    }

    public void CopyText(string value)
    {
    }
}
