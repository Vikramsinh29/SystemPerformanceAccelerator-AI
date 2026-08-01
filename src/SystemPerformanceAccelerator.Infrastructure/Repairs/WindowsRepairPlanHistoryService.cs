using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Diagnostics;

namespace SystemPerformanceAccelerator.Infrastructure.Repairs;

public sealed class WindowsRepairPlanHistoryService :
    IWindowsRepairPlanHistoryService
{
    private const int DefaultMaximumRecordCount = 20;
    private static readonly TimeSpan DefaultMaximumRecordAge =
        TimeSpan.FromDays(90);

    private static readonly JsonSerializerOptions SerializerOptions =
        new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

    private readonly DiagnosticPathSanitizer _sanitizer;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly int _maximumRecordCount;
    private readonly TimeSpan _maximumRecordAge;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public WindowsRepairPlanHistoryService(
        string? planRoot = null,
        DiagnosticPathSanitizer? sanitizer = null,
        Func<DateTimeOffset>? utcNow = null,
        int maximumRecordCount = DefaultMaximumRecordCount,
        TimeSpan? maximumRecordAge = null)
    {
        if (maximumRecordCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumRecordCount));
        }

        PlanRoot = Path.GetFullPath(
            planRoot ??
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "SystemPerformanceAccelerator",
                "repair-assessments",
                "plans"));
        _sanitizer = sanitizer ??
            new DiagnosticPathSanitizer();
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _maximumRecordCount = maximumRecordCount;
        _maximumRecordAge = maximumRecordAge ??
            DefaultMaximumRecordAge;
    }

    public string PlanRoot { get; }

    public async Task SaveAsync(
        WindowsRepairPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        await _writeLock.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(PlanRoot);
            var sanitized = Sanitize(plan);
            var destination = Path.Combine(
                PlanRoot,
                sanitized.ReferenceId + ".json");
            var temporaryPath = destination + ".tmp";

            try
            {
                var json = JsonSerializer.Serialize(
                    sanitized,
                    SerializerOptions);
                await File.WriteAllTextAsync(
                        temporaryPath,
                        json,
                        new UTF8Encoding(
                            encoderShouldEmitUTF8Identifier: false),
                        cancellationToken)
                    .ConfigureAwait(false);
                File.Move(
                    temporaryPath,
                    destination,
                    overwrite: true);
            }
            finally
            {
                TryDeleteFile(temporaryPath);
            }

            ApplyRetention(_utcNow().ToUniversalTime());
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public WindowsRepairPlan? LoadLatest()
    {
        if (!Directory.Exists(PlanRoot))
        {
            return null;
        }

        try
        {
            foreach (var file in Directory
                         .EnumerateFiles(
                             PlanRoot,
                             "PLAN-*.json",
                             SearchOption.TopDirectoryOnly)
                         .Select(path => new FileInfo(path))
                         .OrderByDescending(item =>
                             item.LastWriteTimeUtc))
            {
                try
                {
                    var json = File.ReadAllText(file.FullName);
                    var plan = JsonSerializer.Deserialize<
                        WindowsRepairPlan>(
                        json,
                        SerializerOptions);
                    if (plan is not null)
                    {
                        return Sanitize(plan);
                    }
                }
                catch (Exception ex) when (
                    ex is IOException or
                    UnauthorizedAccessException or
                    JsonException or
                    NotSupportedException)
                {
                }
            }
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException)
        {
        }

        return null;
    }

    public void DeleteHistory()
    {
        try
        {
            if (Directory.Exists(PlanRoot))
            {
                Directory.Delete(
                    PlanRoot,
                    recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (System.Security.SecurityException)
        {
        }
    }

    private WindowsRepairPlan Sanitize(
        WindowsRepairPlan plan) =>
        plan with
        {
            ReferenceId =
                _sanitizer.Sanitize(plan.ReferenceId),
            AssessmentReferenceId =
                _sanitizer.Sanitize(
                    plan.AssessmentReferenceId),
            ApplicationVersion =
                _sanitizer.Sanitize(
                    plan.ApplicationVersion),
            BuildIdentifier =
                _sanitizer.Sanitize(
                    plan.BuildIdentifier),
            DecisionTitle =
                _sanitizer.Sanitize(
                    plan.DecisionTitle),
            Summary = _sanitizer.Sanitize(plan.Summary),
            Disclosure =
                _sanitizer.Sanitize(plan.Disclosure),
            Preflight = plan.Preflight
                .Select(item => item with
                {
                    Title = _sanitizer.Sanitize(
                        item.Title),
                    Detail = _sanitizer.Sanitize(
                        item.Detail)
                })
                .ToArray(),
            Steps = plan.Steps
                .Select(step => step with
                {
                    Title = _sanitizer.Sanitize(
                        step.Title),
                    Purpose = _sanitizer.Sanitize(
                        step.Purpose)
                })
                .ToArray()
        };

    private void ApplyRetention(
        DateTimeOffset currentUtc)
    {
        if (!Directory.Exists(PlanRoot))
        {
            return;
        }

        try
        {
            var cutoff =
                currentUtc.UtcDateTime -
                _maximumRecordAge;
            var records = Directory
                .EnumerateFiles(
                    PlanRoot,
                    "*.json",
                    SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .ToArray();

            foreach (var oldRecord in records.Where(
                         record =>
                             record.LastWriteTimeUtc < cutoff))
            {
                TryDeleteFile(oldRecord.FullName);
            }

            records = Directory
                .EnumerateFiles(
                    PlanRoot,
                    "*.json",
                    SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(record =>
                    record.LastWriteTimeUtc)
                .ToArray();

            foreach (var excessRecord in records.Skip(
                         _maximumRecordCount))
            {
                TryDeleteFile(excessRecord.FullName);
            }
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException)
        {
        }
    }

    private static void TryDeleteFile(string path)
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
