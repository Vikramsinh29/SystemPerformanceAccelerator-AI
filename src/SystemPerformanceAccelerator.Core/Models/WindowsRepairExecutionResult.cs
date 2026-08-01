namespace SystemPerformanceAccelerator.Core.Models;

public sealed record WindowsRepairExecutionResult(
    string ReferenceId,
    string AssessmentReferenceId,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    string ApplicationVersion,
    string BuildIdentifier,
    WindowsRepairExecutionOutcome Outcome,
    string Summary,
    IReadOnlyList<WindowsRepairExecutionStepResult> Steps,
    WindowsRepairAssessmentResult? VerificationAssessment,
    bool StopRequested,
    bool AutomaticRestartAttempted,
    IReadOnlyList<string> Issues)
{
    public TimeSpan Duration => FinishedUtc - StartedUtc;
}
