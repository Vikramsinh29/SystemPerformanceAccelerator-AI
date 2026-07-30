namespace SystemPerformanceAccelerator.Core.Models;

public sealed record AutoCleanSchedule(
    Guid Id,
    string Name,
    bool IsEnabled,
    AutoCleanScheduleFrequency Frequency,
    TimeOnly RunAtLocalTime,
    DayOfWeek WeeklyDay,
    int MonthlyDay,
    IReadOnlyCollection<CustomCleanCategory> Categories)
{
    public const int MaximumNameLength = 80;
}
