using System.Text.Json;
using System.Text.Json.Serialization;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Services;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class AutoCleanScheduleServiceTests
{
    [Fact]
    public void Load_WhenFileDoesNotExist_ReturnsEmptyWithoutWarning()
    {
        using var location = new TemporaryScheduleLocation();
        var service = new AutoCleanScheduleService(location.SchedulesPath);

        var result = service.Load();

        Assert.Empty(result.Schedules);
        Assert.False(result.HasWarning);
        Assert.False(File.Exists(location.SchedulesPath));
    }

    [Fact]
    public void SaveAndLoad_RoundTripsSupportedSchedule()
    {
        using var location = new TemporaryScheduleLocation();
        var service = new AutoCleanScheduleService(location.SchedulesPath);
        var expected = CreateSchedule(
            Guid.NewGuid(),
            "Weekly temporary files",
            true,
            AutoCleanScheduleFrequency.Weekly,
            new TimeOnly(9, 30),
            DayOfWeek.Friday,
            15);

        service.Save([expected]);
        var result = service.Load();

        var actual = Assert.Single(result.Schedules);
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.IsEnabled, actual.IsEnabled);
        Assert.Equal(expected.Frequency, actual.Frequency);
        Assert.Equal(expected.RunAtLocalTime, actual.RunAtLocalTime);
        Assert.Equal(expected.WeeklyDay, actual.WeeklyDay);
        Assert.Equal(expected.MonthlyDay, actual.MonthlyDay);
        Assert.Equal(expected.Categories, actual.Categories);
        Assert.False(result.HasWarning);
    }

    [Fact]
    public void SaveAndLoad_PreservesMultipleDistinctSchedules()
    {
        using var location = new TemporaryScheduleLocation();
        var service = new AutoCleanScheduleService(location.SchedulesPath);
        var daily = CreateSchedule(
            Guid.NewGuid(),
            "Daily plan",
            true,
            AutoCleanScheduleFrequency.Daily,
            new TimeOnly(8, 0),
            DayOfWeek.Monday,
            1);
        var weekly = CreateSchedule(
            Guid.NewGuid(),
            "Weekly plan",
            false,
            AutoCleanScheduleFrequency.Weekly,
            new TimeOnly(18, 30),
            DayOfWeek.Saturday,
            1);
        var monthly = CreateSchedule(
            Guid.NewGuid(),
            "Monthly plan",
            true,
            AutoCleanScheduleFrequency.Monthly,
            new TimeOnly(10, 15),
            DayOfWeek.Monday,
            20);

        service.Save([daily, weekly, monthly]);
        var result = service.Load();

        Assert.Equal(3, result.Schedules.Count);
        Assert.Equal(
            [daily.Id, weekly.Id, monthly.Id],
            result.Schedules.Select(schedule => schedule.Id));
        Assert.Equal(
            ["Daily plan", "Weekly plan", "Monthly plan"],
            result.Schedules.Select(schedule => schedule.Name));
    }

    [Fact]
    public void Load_WhenJsonIsMalformed_ReturnsEmptyWithWarning()
    {
        using var location = new TemporaryScheduleLocation();
        Directory.CreateDirectory(Path.GetDirectoryName(location.SchedulesPath)!);
        File.WriteAllText(location.SchedulesPath, "{ invalid json");
        var service = new AutoCleanScheduleService(location.SchedulesPath);

        var result = service.Load();

        Assert.Empty(result.Schedules);
        Assert.True(result.HasWarning);
    }

    [Fact]
    public void Save_NormalizesUnsafeValuesAndDisablesUnsupportedCategory()
    {
        using var location = new TemporaryScheduleLocation();
        var service = new AutoCleanScheduleService(location.SchedulesPath);
        var unsafeSchedule = new AutoCleanSchedule(
            Guid.Empty,
            new string('A', AutoCleanSchedule.MaximumNameLength + 10),
            true,
            (AutoCleanScheduleFrequency)999,
            new TimeOnly(7, 15),
            (DayOfWeek)999,
            99,
            [(CustomCleanCategory)999]);

        service.Save([unsafeSchedule]);
        var result = service.Load();

        var actual = Assert.Single(result.Schedules);
        Assert.NotEqual(Guid.Empty, actual.Id);
        Assert.Equal(AutoCleanSchedule.MaximumNameLength, actual.Name.Length);
        Assert.False(actual.IsEnabled);
        Assert.Equal(AutoCleanScheduleFrequency.Daily, actual.Frequency);
        Assert.Equal(DayOfWeek.Monday, actual.WeeklyDay);
        Assert.Equal(31, actual.MonthlyDay);
        Assert.Empty(actual.Categories);
    }

    [Fact]
    public void Save_WhenScheduleLimitIsExceeded_ThrowsWithoutCreatingFile()
    {
        using var location = new TemporaryScheduleLocation();
        var service = new AutoCleanScheduleService(location.SchedulesPath);
        var schedules = Enumerable.Range(
                0,
                AutoCleanScheduleService.MaximumScheduleCount + 1)
            .Select(index => CreateSchedule(
                Guid.NewGuid(),
                $"Schedule {index}",
                false,
                AutoCleanScheduleFrequency.Daily,
                new TimeOnly(9, 0),
                DayOfWeek.Monday,
                1))
            .ToArray();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.Save(schedules));
        Assert.False(File.Exists(location.SchedulesPath));
    }

    [Fact]
    public void CalculateNextRun_DisabledSchedule_ReturnsNull()
    {
        var service = new AutoCleanScheduleService();
        var schedule = CreateSchedule(
            Guid.NewGuid(),
            "Disabled",
            false,
            AutoCleanScheduleFrequency.Daily,
            new TimeOnly(9, 0),
            DayOfWeek.Monday,
            1);

        var result = service.CalculateNextRun(
            schedule,
            new DateTime(2026, 7, 30, 8, 0, 0));

        Assert.Null(result);
    }

    [Fact]
    public void CalculateNextRun_DailyBeforeTime_ReturnsToday()
    {
        var service = new AutoCleanScheduleService();
        var schedule = CreateSchedule(
            Guid.NewGuid(),
            "Daily",
            true,
            AutoCleanScheduleFrequency.Daily,
            new TimeOnly(9, 0),
            DayOfWeek.Monday,
            1);

        var result = service.CalculateNextRun(
            schedule,
            new DateTime(2026, 7, 30, 8, 0, 0));

        Assert.Equal(new DateTime(2026, 7, 30, 9, 0, 0), result);
    }

    [Fact]
    public void CalculateNextRun_DailyAtOrAfterTime_ReturnsTomorrow()
    {
        var service = new AutoCleanScheduleService();
        var schedule = CreateSchedule(
            Guid.NewGuid(),
            "Daily",
            true,
            AutoCleanScheduleFrequency.Daily,
            new TimeOnly(9, 0),
            DayOfWeek.Monday,
            1);

        var result = service.CalculateNextRun(
            schedule,
            new DateTime(2026, 7, 30, 9, 0, 0));

        Assert.Equal(new DateTime(2026, 7, 31, 9, 0, 0), result);
    }

    [Fact]
    public void CalculateNextRun_WeeklyFutureDay_ReturnsSameWeek()
    {
        var service = new AutoCleanScheduleService();
        var schedule = CreateSchedule(
            Guid.NewGuid(),
            "Weekly",
            true,
            AutoCleanScheduleFrequency.Weekly,
            new TimeOnly(10, 0),
            DayOfWeek.Friday,
            1);

        var result = service.CalculateNextRun(
            schedule,
            new DateTime(2026, 7, 30, 8, 0, 0));

        Assert.Equal(new DateTime(2026, 7, 31, 10, 0, 0), result);
    }

    [Fact]
    public void CalculateNextRun_WeeklySameDayAfterTime_ReturnsNextWeek()
    {
        var service = new AutoCleanScheduleService();
        var schedule = CreateSchedule(
            Guid.NewGuid(),
            "Weekly",
            true,
            AutoCleanScheduleFrequency.Weekly,
            new TimeOnly(7, 0),
            DayOfWeek.Thursday,
            1);

        var result = service.CalculateNextRun(
            schedule,
            new DateTime(2026, 7, 30, 8, 0, 0));

        Assert.Equal(new DateTime(2026, 8, 6, 7, 0, 0), result);
    }

    [Fact]
    public void CalculateNextRun_MonthlyFutureDay_ReturnsCurrentMonth()
    {
        var service = new AutoCleanScheduleService();
        var schedule = CreateSchedule(
            Guid.NewGuid(),
            "Monthly",
            true,
            AutoCleanScheduleFrequency.Monthly,
            new TimeOnly(9, 45),
            DayOfWeek.Monday,
            31);

        var result = service.CalculateNextRun(
            schedule,
            new DateTime(2026, 7, 30, 8, 0, 0));

        Assert.Equal(new DateTime(2026, 7, 31, 9, 45, 0), result);
    }

    [Fact]
    public void CalculateNextRun_MonthlyUsesLastValidDayOfNextMonth()
    {
        var service = new AutoCleanScheduleService();
        var schedule = CreateSchedule(
            Guid.NewGuid(),
            "Monthly",
            true,
            AutoCleanScheduleFrequency.Monthly,
            new TimeOnly(9, 45),
            DayOfWeek.Monday,
            31);

        var result = service.CalculateNextRun(
            schedule,
            new DateTime(2026, 1, 31, 10, 0, 0));

        Assert.Equal(new DateTime(2026, 2, 28, 9, 45, 0), result);
    }

    [Fact]
    public void CalculateNextRun_UnknownFrequency_FailsClosed()
    {
        var service = new AutoCleanScheduleService();
        var schedule = CreateSchedule(
            Guid.NewGuid(),
            "Unknown",
            true,
            (AutoCleanScheduleFrequency)999,
            new TimeOnly(9, 0),
            DayOfWeek.Monday,
            1);

        var result = service.CalculateNextRun(
            schedule,
            new DateTime(2026, 7, 30, 8, 0, 0));

        Assert.Null(result);
    }

    private static AutoCleanSchedule CreateSchedule(
        Guid id,
        string name,
        bool isEnabled,
        AutoCleanScheduleFrequency frequency,
        TimeOnly runAtLocalTime,
        DayOfWeek weeklyDay,
        int monthlyDay) =>
        new(
            id,
            name,
            isEnabled,
            frequency,
            runAtLocalTime,
            weeklyDay,
            monthlyDay,
            [CustomCleanCategory.TemporaryFiles]);

    private sealed class TemporaryScheduleLocation : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            $"spa-auto-clean-schedule-tests-{Guid.NewGuid():N}");

        public string SchedulesPath =>
            Path.Combine(_directory, "auto-clean-schedules.json");

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_directory))
                {
                    Directory.Delete(_directory, true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
