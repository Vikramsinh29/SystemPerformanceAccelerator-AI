using System.Text.Json;
using System.Text.Json.Serialization;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Services;

public sealed class AutoCleanScheduleService : IAutoCleanScheduleService
{
    public const int MaximumScheduleCount = 50;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public AutoCleanScheduleService(string? schedulesPath = null)
    {
        SchedulesPath = string.IsNullOrWhiteSpace(schedulesPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SystemPerformanceAccelerator",
                "auto-clean-schedules.json")
            : Path.GetFullPath(schedulesPath);
    }

    public string SchedulesPath { get; }

    public AutoCleanScheduleLoadResult Load()
    {
        if (!File.Exists(SchedulesPath))
        {
            return new AutoCleanScheduleLoadResult([], string.Empty);
        }

        try
        {
            var json = File.ReadAllText(SchedulesPath);
            var stored = JsonSerializer.Deserialize<List<AutoCleanSchedule>>(
                json,
                SerializerOptions);

            if (stored is null)
            {
                return EmptyWithWarning(
                    "The local Auto Clean schedule file was empty. No schedules were loaded.");
            }

            var normalized = NormalizeSchedules(stored, out var changed);
            var warning = changed
                ? "One or more invalid Auto Clean schedule values were replaced with safe values."
                : string.Empty;

            return new AutoCleanScheduleLoadResult(normalized, warning);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            JsonException or
            NotSupportedException)
        {
            return EmptyWithWarning(
                "The local Auto Clean schedule file could not be read. No schedules were loaded.");
        }
    }

    public void Save(IReadOnlyCollection<AutoCleanSchedule> schedules)
    {
        ArgumentNullException.ThrowIfNull(schedules);

        if (schedules.Count > MaximumScheduleCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schedules),
                $"A maximum of {MaximumScheduleCount} schedules is supported.");
        }

        var normalized = NormalizeSchedules(schedules, out _);
        var directory = Path.GetDirectoryName(SchedulesPath)
            ?? throw new InvalidOperationException(
                "The Auto Clean schedule path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = SchedulesPath + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(normalized, SerializerOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, SchedulesPath, true);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    public DateTime? CalculateNextRun(
        AutoCleanSchedule schedule,
        DateTime localNow)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (!schedule.IsEnabled ||
            !Enum.IsDefined(schedule.Frequency))
        {
            return null;
        }

        return schedule.Frequency switch
        {
            AutoCleanScheduleFrequency.Daily =>
                CalculateDaily(schedule.RunAtLocalTime, localNow),
            AutoCleanScheduleFrequency.Weekly when Enum.IsDefined(schedule.WeeklyDay) =>
                CalculateWeekly(
                    schedule.RunAtLocalTime,
                    schedule.WeeklyDay,
                    localNow),
            AutoCleanScheduleFrequency.Monthly when
                schedule.MonthlyDay is >= 1 and <= 31 =>
                CalculateMonthly(
                    schedule.RunAtLocalTime,
                    schedule.MonthlyDay,
                    localNow),
            _ => null
        };
    }

    private static IReadOnlyCollection<AutoCleanSchedule> NormalizeSchedules(
        IEnumerable<AutoCleanSchedule> schedules,
        out bool changed)
    {
        var source = schedules.ToArray();
        var normalized = new List<AutoCleanSchedule>();
        var identifiers = new HashSet<Guid>();
        changed = false;

        foreach (var schedule in source.Take(MaximumScheduleCount))
        {
            if (schedule is null)
            {
                changed = true;
                continue;
            }

            var identifier = schedule.Id;
            if (identifier == Guid.Empty || !identifiers.Add(identifier))
            {
                identifier = Guid.NewGuid();
                identifiers.Add(identifier);
                changed = true;
            }

            var name = string.IsNullOrWhiteSpace(schedule.Name)
                ? "Auto Clean Schedule"
                : schedule.Name.Trim();
            if (name.Length > AutoCleanSchedule.MaximumNameLength)
            {
                name = name[..AutoCleanSchedule.MaximumNameLength];
            }

            var frequency = Enum.IsDefined(schedule.Frequency)
                ? schedule.Frequency
                : AutoCleanScheduleFrequency.Daily;
            var weeklyDay = Enum.IsDefined(schedule.WeeklyDay)
                ? schedule.WeeklyDay
                : DayOfWeek.Monday;
            var monthlyDay = Math.Clamp(schedule.MonthlyDay, 1, 31);
            var categories = (schedule.Categories ?? Array.Empty<CustomCleanCategory>())
                .Where(category =>
                    category == CustomCleanCategory.TemporaryFiles)
                .Distinct()
                .ToArray();
            var isEnabled = schedule.IsEnabled && categories.Length > 0;

            var lastManualRun = NormalizeManualRun(
                schedule.LastManualRun,
                out var manualRunChanged);
            var normalizedSchedule = new AutoCleanSchedule(
                identifier,
                name,
                isEnabled,
                frequency,
                schedule.RunAtLocalTime,
                weeklyDay,
                monthlyDay,
                categories)
            {
                LastManualRun = lastManualRun
            };

            if (manualRunChanged ||
                identifier != schedule.Id ||
                name != schedule.Name ||
                isEnabled != schedule.IsEnabled ||
                frequency != schedule.Frequency ||
                weeklyDay != schedule.WeeklyDay ||
                monthlyDay != schedule.MonthlyDay ||
                !categories.SequenceEqual(schedule.Categories ?? Array.Empty<CustomCleanCategory>()))
            {
                changed = true;
            }

            normalized.Add(normalizedSchedule);
        }

        if (source.Length > MaximumScheduleCount)
        {
            changed = true;
        }

        return normalized;
    }

    private static AutoCleanManualRunSummary? NormalizeManualRun(
        AutoCleanManualRunSummary? summary,
        out bool changed)
    {
        changed = false;
        if (summary is null)
        {
            return null;
        }

        var requestedCount = Math.Max(0, summary.RequestedCount);
        var deletedCount = Math.Max(0, summary.DeletedCount);
        var skippedCount = Math.Max(0, summary.SkippedCount);
        var failedCount = Math.Max(0, summary.FailedCount);
        var reclaimedBytes = Math.Max(0, summary.ReclaimedBytes);
        var elapsed = summary.Elapsed < TimeSpan.Zero
            ? TimeSpan.Zero
            : summary.Elapsed;
        var firstIssue = (summary.FirstIssue ?? string.Empty).Trim();
        if (firstIssue.Length > AutoCleanManualRunSummary.MaximumFirstIssueLength)
        {
            firstIssue = firstIssue[..AutoCleanManualRunSummary.MaximumFirstIssueLength];
        }

        var normalized = new AutoCleanManualRunSummary(
            summary.CompletedAtLocal,
            requestedCount,
            deletedCount,
            skippedCount,
            failedCount,
            reclaimedBytes,
            elapsed,
            firstIssue);
        changed = normalized != summary;
        return normalized;
    }

    private static DateTime CalculateDaily(
        TimeOnly runAtLocalTime,
        DateTime localNow)
    {
        var candidate = localNow.Date.Add(runAtLocalTime.ToTimeSpan());
        return candidate > localNow
            ? candidate
            : candidate.AddDays(1);
    }

    private static DateTime CalculateWeekly(
        TimeOnly runAtLocalTime,
        DayOfWeek weeklyDay,
        DateTime localNow)
    {
        var daysUntil = ((int)weeklyDay - (int)localNow.DayOfWeek + 7) % 7;
        var candidate = localNow.Date
            .AddDays(daysUntil)
            .Add(runAtLocalTime.ToTimeSpan());

        return candidate > localNow
            ? candidate
            : candidate.AddDays(7);
    }

    private static DateTime CalculateMonthly(
        TimeOnly runAtLocalTime,
        int monthlyDay,
        DateTime localNow)
    {
        var candidate = CreateMonthlyCandidate(
            localNow.Year,
            localNow.Month,
            monthlyDay,
            runAtLocalTime);

        if (candidate > localNow)
        {
            return candidate;
        }

        var nextMonth = localNow.Date.AddMonths(1);
        return CreateMonthlyCandidate(
            nextMonth.Year,
            nextMonth.Month,
            monthlyDay,
            runAtLocalTime);
    }

    private static DateTime CreateMonthlyCandidate(
        int year,
        int month,
        int monthlyDay,
        TimeOnly runAtLocalTime)
    {
        var safeDay = Math.Min(
            monthlyDay,
            DateTime.DaysInMonth(year, month));
        return new DateTime(year, month, safeDay)
            .Add(runAtLocalTime.ToTimeSpan());
    }

    private static AutoCleanScheduleLoadResult EmptyWithWarning(string warning) =>
        new([], warning);

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
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
