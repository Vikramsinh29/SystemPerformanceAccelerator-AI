namespace SystemPerformanceAccelerator.Core.Models;

public sealed record DiagnosticFeedbackSubmissionResult(
    bool Success,
    string? Reference,
    string Message);
