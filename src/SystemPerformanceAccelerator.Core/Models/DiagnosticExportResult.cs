namespace SystemPerformanceAccelerator.Core.Models;

public sealed record DiagnosticExportResult(
    bool Success,
    string ExportPath,
    int EventCount,
    string Message);
