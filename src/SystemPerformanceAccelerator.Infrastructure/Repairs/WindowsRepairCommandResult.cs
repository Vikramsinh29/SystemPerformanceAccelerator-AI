namespace SystemPerformanceAccelerator.Infrastructure.Repairs;

public sealed record WindowsRepairCommandResult(
    bool Started,
    int? ExitCode,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    string StandardOutput,
    string StandardError,
    string StartFailure)
{
    public TimeSpan Duration => FinishedUtc - StartedUtc;
}
