namespace SystemPerformanceAccelerator.Core.Models;

public sealed record WindowsRepairAssessmentResult(
    string ReferenceId,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    string ApplicationVersion,
    string BuildIdentifier,
    WindowsRepairEnvironmentStatus Environment,
    IReadOnlyList<WindowsRepairCheckResult> Checks,
    WindowsRepairAssessmentOutcome OverallOutcome,
    bool StopRequested,
    IReadOnlyList<string> Issues)
{
    public TimeSpan Duration => FinishedUtc - StartedUtc;
}
