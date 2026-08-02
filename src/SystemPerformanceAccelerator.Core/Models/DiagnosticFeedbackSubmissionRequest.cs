namespace SystemPerformanceAccelerator.Core.Models;

public sealed record DiagnosticFeedbackSubmissionRequest(
    int SchemaVersion,
    string ApplicationVersion,
    string BuildIdentifier,
    string ErrorReference,
    string AffectedArea,
    string Description,
    string ExpectedResult,
    string WindowsVersion,
    string RuntimeVersion,
    bool IsElevated,
    string InstallationId,
    IReadOnlyList<DiagnosticFeedbackEvent> DiagnosticEvents);

public sealed record DiagnosticFeedbackEvent(
    string Reference,
    string Type,
    string Message,
    string StackTrace);
