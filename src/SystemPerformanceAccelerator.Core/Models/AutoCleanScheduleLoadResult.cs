namespace SystemPerformanceAccelerator.Core.Models;

public sealed record AutoCleanScheduleLoadResult(
    IReadOnlyCollection<AutoCleanSchedule> Schedules,
    string Warning)
{
    public bool HasWarning => !string.IsNullOrWhiteSpace(Warning);
}
