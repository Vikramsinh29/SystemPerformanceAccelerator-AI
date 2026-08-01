namespace SystemPerformanceAccelerator.Core.Models;

public sealed record WindowsRepairExecutionStepResult(
    WindowsRepairExecutionStep Step,
    WindowsRepairExecutionStepOutcome Outcome,
    string Title,
    string Summary,
    bool ChangesWindows,
    string ExecutableName,
    IReadOnlyList<string> Arguments,
    int? ExitCode,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    string SanitizedOutput,
    string SanitizedError)
{
    public TimeSpan Duration => FinishedUtc - StartedUtc;
}
