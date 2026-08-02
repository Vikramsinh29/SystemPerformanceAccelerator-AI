namespace SystemPerformanceAccelerator.Core.Models;

public sealed record DiagnosticFeedbackRequest(
    string ErrorReference,
    string AffectedArea,
    string Description,
    string ExpectedResult,
    bool IncludeSanitizedDiagnostics);
