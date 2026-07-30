using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface IAutoCleanScheduleService
{
    string SchedulesPath { get; }

    AutoCleanScheduleLoadResult Load();

    void Save(IReadOnlyCollection<AutoCleanSchedule> schedules);

    DateTime? CalculateNextRun(
        AutoCleanSchedule schedule,
        DateTime localNow);
}
