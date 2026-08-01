namespace SystemPerformanceAccelerator.Core.Models;

public sealed record DiagnosticExportPreview(
    int EventCount,
    IReadOnlyList<string> ErrorReferences,
    bool IncludesHardwareSummary,
    string DiagnosticsRoot,
    string PrivacyNotice)
{
    public bool HasEvents => EventCount > 0;
}
