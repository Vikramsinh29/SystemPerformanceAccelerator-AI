namespace SystemPerformanceAccelerator.Core.Models;

public sealed record WindowsRepairCheckResult(
    WindowsRepairAssessmentCheck Check,
    WindowsRepairAssessmentOutcome Outcome,
    string Title,
    string Summary,
    string ExecutableName,
    IReadOnlyList<string> Arguments,
    int? ExitCode,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    string SanitizedOutput,
    string SanitizedError,
    bool UserStopRequested,
    string Limitation)
{
    public TimeSpan Duration => FinishedUtc - StartedUtc;
}
