namespace SystemPerformanceAccelerator.Core.Models;

public sealed record WindowsRepairExecutionReadiness(
    string PlanReferenceId,
    string AssessmentReferenceId,
    DateTimeOffset EvaluatedUtc,
    bool IsReady,
    string Title,
    string Summary,
    long MinimumFreeBytes,
    long? AvailableFreeBytes,
    IReadOnlyList<string> Issues);
