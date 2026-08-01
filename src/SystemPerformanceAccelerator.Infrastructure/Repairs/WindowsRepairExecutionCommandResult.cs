namespace SystemPerformanceAccelerator.Infrastructure.Repairs;

public sealed record WindowsRepairExecutionCommandResult(
    bool Started,
    int? ExitCode,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    string StandardOutput,
    string StandardError,
    string StartFailure);
