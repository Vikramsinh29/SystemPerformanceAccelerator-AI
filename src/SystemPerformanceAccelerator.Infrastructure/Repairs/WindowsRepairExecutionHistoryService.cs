using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Diagnostics;

namespace SystemPerformanceAccelerator.Infrastructure.Repairs;

public sealed class WindowsRepairExecutionHistoryService :
    IWindowsRepairExecutionHistoryService
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
    private readonly WindowsRepairExecutionReportExporter
        _exporter;
    private readonly SemaphoreSlim _writeLock =
        new(1, 1);

    public WindowsRepairExecutionHistoryService(
        string? executionRoot = null,
        DiagnosticPathSanitizer? sanitizer = null,
        Func<DateTimeOffset>? utcNow = null,
        int maximumRecordCount =
            DefaultMaximumRecordCount,
        TimeSpan? maximumRecordAge = null,
        WindowsRepairExecutionReportExporter? exporter = null)
    {
        if (maximumRecordCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumRecordCount));
        }

        ExecutionRoot = Path.GetFullPath(
            executionRoot ??
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder
                        .LocalApplicationData),
                "SystemPerformanceAccelerator",
                "repair-assessments",
                "executions"));
        _sanitizer = sanitizer ??
            new DiagnosticPathSanitizer();
        _utcNow = utcNow ??
            (() => DateTimeOffset.UtcNow);
        _maximumRecordCount = maximumRecordCount;
        _maximumRecordAge = maximumRecordAge ??
            DefaultMaximumRecordAge;
        _exporter = exporter ??
            new WindowsRepairExecutionReportExporter();
    }

    public string ExecutionRoot { get; }

    public async Task SaveAsync(
        WindowsRepairExecutionResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        await _writeLock.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(ExecutionRoot);
            var sanitized = Sanitize(result);
            var destination = Path.Combine(
                ExecutionRoot,
                sanitized.ReferenceId + ".json");
            var temporaryPath =
                destination + ".tmp";

            try
            {
                var json = JsonSerializer.Serialize(
                    sanitized,
                    SerializerOptions);
                await File.WriteAllTextAsync(
                        temporaryPath,
                        json,
                        new UTF8Encoding(
                            encoderShouldEmitUTF8Identifier:
                                false),
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

            ApplyRetention(
                _utcNow().ToUniversalTime());
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public WindowsRepairExecutionResult? LoadLatest()
    {
        if (!Directory.Exists(ExecutionRoot))
        {
            return null;
        }

        try
        {
            foreach (var file in Directory
                         .EnumerateFiles(
                             ExecutionRoot,
                             "REPAIR-*.json",
                             SearchOption.TopDirectoryOnly)
                         .Select(path =>
                             new FileInfo(path))
                         .OrderByDescending(item =>
                             item.LastWriteTimeUtc))
            {
                try
                {
                    var json =
                        File.ReadAllText(file.FullName);
                    var result =
                        JsonSerializer.Deserialize<
                            WindowsRepairExecutionResult>(
                            json,
                            SerializerOptions);

                    if (result is not null)
                    {
                        return Sanitize(result);
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

    public async Task<string?> ExportLatestAsync(
        string destinationZipPath,
        CancellationToken cancellationToken = default)
    {
        var latest = LoadLatest();
        if (latest is null)
        {
            return null;
        }

        return await _exporter.ExportAsync(
                latest,
                destinationZipPath,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public void DeleteHistory()
    {
        try
        {
            if (Directory.Exists(ExecutionRoot))
            {
                Directory.Delete(
                    ExecutionRoot,
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

    private WindowsRepairExecutionResult Sanitize(
        WindowsRepairExecutionResult result) =>
        result with
        {
            ReferenceId =
                _sanitizer.Sanitize(
                    result.ReferenceId),
            AssessmentReferenceId =
                _sanitizer.Sanitize(
                    result.AssessmentReferenceId),
            ApplicationVersion =
                _sanitizer.Sanitize(
                    result.ApplicationVersion),
            BuildIdentifier =
                _sanitizer.Sanitize(
                    result.BuildIdentifier),
            Summary =
                _sanitizer.Sanitize(result.Summary),
            Issues = result.Issues
                .Select(_sanitizer.Sanitize)
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item))
                .ToArray(),
            Steps = result.Steps
                .Select(step => step with
                {
                    Title =
                        _sanitizer.Sanitize(
                            step.Title),
                    Summary =
                        _sanitizer.Sanitize(
                            step.Summary),
                    ExecutableName =
                        _sanitizer.Sanitize(
                            step.ExecutableName),
                    Arguments = step.Arguments
                        .Select(_sanitizer.Sanitize)
                        .ToArray(),
                    SanitizedOutput =
                        _sanitizer.Sanitize(
                            step.SanitizedOutput),
                    SanitizedError =
                        _sanitizer.Sanitize(
                            step.SanitizedError)
                })
                .ToArray(),
            VerificationAssessment =
                result.VerificationAssessment is null
                    ? null
                    : SanitizeAssessment(
                        result.VerificationAssessment)
        };

    private WindowsRepairAssessmentResult
        SanitizeAssessment(
            WindowsRepairAssessmentResult assessment) =>
        assessment with
        {
            ReferenceId =
                _sanitizer.Sanitize(
                    assessment.ReferenceId),
            ApplicationVersion =
                _sanitizer.Sanitize(
                    assessment.ApplicationVersion),
            BuildIdentifier =
                _sanitizer.Sanitize(
                    assessment.BuildIdentifier),
            Environment = assessment.Environment with
            {
                WindowsDescription =
                    _sanitizer.Sanitize(
                        assessment.Environment
                            .WindowsDescription),
                WindowsDirectory =
                    _sanitizer.Sanitize(
                        assessment.Environment
                            .WindowsDirectory),
                SystemDriveRoot =
                    _sanitizer.Sanitize(
                        assessment.Environment
                            .SystemDriveRoot),
                Issues = assessment.Environment.Issues
                    .Select(_sanitizer.Sanitize)
                    .ToArray()
            },
            Checks = assessment.Checks
                .Select(check => check with
                {
                    Title =
                        _sanitizer.Sanitize(
                            check.Title),
                    Summary =
                        _sanitizer.Sanitize(
                            check.Summary),
                    ExecutableName =
                        _sanitizer.Sanitize(
                            check.ExecutableName),
                    Arguments = check.Arguments
                        .Select(_sanitizer.Sanitize)
                        .ToArray(),
                    SanitizedOutput =
                        _sanitizer.Sanitize(
                            check.SanitizedOutput),
                    SanitizedError =
                        _sanitizer.Sanitize(
                            check.SanitizedError),
                    Limitation =
                        _sanitizer.Sanitize(
                            check.Limitation)
                })
                .ToArray(),
            Issues = assessment.Issues
                .Select(_sanitizer.Sanitize)
                .ToArray()
        };

    private void ApplyRetention(
        DateTimeOffset currentUtc)
    {
        if (!Directory.Exists(ExecutionRoot))
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
                    ExecutionRoot,
                    "*.json",
                    SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .ToArray();

            foreach (var oldRecord in records.Where(
                         record =>
                             record.LastWriteTimeUtc <
                             cutoff))
            {
                TryDeleteFile(oldRecord.FullName);
            }

            records = Directory
                .EnumerateFiles(
                    ExecutionRoot,
                    "*.json",
                    SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(record =>
                    record.LastWriteTimeUtc)
                .ToArray();

            foreach (var excessRecord in records.Skip(
                         _maximumRecordCount))
            {
                TryDeleteFile(
                    excessRecord.FullName);
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
