using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Diagnostics;

namespace SystemPerformanceAccelerator.Infrastructure.Repairs;

public sealed class WindowsRepairAssessmentHistoryService :
    IWindowsRepairAssessmentHistoryService
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

    private readonly string _recordsDirectory;
    private readonly WindowsRepairAssessmentReportExporter _exporter;
    private readonly DiagnosticPathSanitizer _sanitizer;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly int _maximumRecordCount;
    private readonly TimeSpan _maximumRecordAge;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public WindowsRepairAssessmentHistoryService(
        string? assessmentRoot = null,
        DiagnosticPathSanitizer? sanitizer = null,
        Func<DateTimeOffset>? utcNow = null,
        int maximumRecordCount = DefaultMaximumRecordCount,
        TimeSpan? maximumRecordAge = null,
        WindowsRepairAssessmentReportExporter? exporter = null)
    {
        if (maximumRecordCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumRecordCount));
        }

        AssessmentRoot = Path.GetFullPath(
            assessmentRoot ??
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "SystemPerformanceAccelerator",
                "repair-assessments"));
        _recordsDirectory = Path.Combine(
            AssessmentRoot,
            "records");
        _sanitizer = sanitizer ?? new DiagnosticPathSanitizer();
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _maximumRecordCount = maximumRecordCount;
        _maximumRecordAge = maximumRecordAge ??
            DefaultMaximumRecordAge;
        _exporter = exporter ??
            new WindowsRepairAssessmentReportExporter();
    }

    public string AssessmentRoot { get; }

    public async Task SaveAsync(
        WindowsRepairAssessmentResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        await _writeLock.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_recordsDirectory);
            var sanitized = Sanitize(result);
            var destination = Path.Combine(
                _recordsDirectory,
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

    public WindowsRepairAssessmentResult? LoadLatest() =>
        LoadRecent(1).FirstOrDefault();

    public IReadOnlyList<WindowsRepairAssessmentResult> LoadRecent(
        int maximumCount = 20)
    {
        if (maximumCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumCount));
        }

        if (!Directory.Exists(_recordsDirectory))
        {
            return Array.Empty<WindowsRepairAssessmentResult>();
        }

        var results = new List<WindowsRepairAssessmentResult>();
        try
        {
            foreach (var file in Directory
                         .EnumerateFiles(
                             _recordsDirectory,
                             "ASSESS-*.json",
                             SearchOption.TopDirectoryOnly)
                         .Select(path => new FileInfo(path))
                         .OrderByDescending(
                             item => item.LastWriteTimeUtc))
            {
                try
                {
                    var json = File.ReadAllText(file.FullName);
                    var result = JsonSerializer.Deserialize<
                        WindowsRepairAssessmentResult>(
                        json,
                        SerializerOptions);
                    if (result is not null)
                    {
                        results.Add(Sanitize(result));
                        if (results.Count >= maximumCount)
                        {
                            break;
                        }
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

        return results;
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
            if (Directory.Exists(AssessmentRoot))
            {
                Directory.Delete(
                    AssessmentRoot,
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

    private WindowsRepairAssessmentResult Sanitize(
        WindowsRepairAssessmentResult result)
    {
        var environment = result.Environment with
        {
            WindowsDescription = _sanitizer.Sanitize(
                result.Environment.WindowsDescription),
            WindowsDirectory = _sanitizer.Sanitize(
                result.Environment.WindowsDirectory),
            SystemDriveRoot = _sanitizer.Sanitize(
                result.Environment.SystemDriveRoot),
            Issues = result.Environment.Issues
                .Select(_sanitizer.Sanitize)
                .ToArray()
        };

        var checks = result.Checks
            .Select(check => check with
            {
                Title = _sanitizer.Sanitize(check.Title),
                Summary = _sanitizer.Sanitize(check.Summary),
                ExecutableName = _sanitizer.Sanitize(
                    check.ExecutableName),
                Arguments = check.Arguments
                    .Select(_sanitizer.Sanitize)
                    .ToArray(),
                SanitizedOutput = _sanitizer.Sanitize(
                    check.SanitizedOutput),
                SanitizedError = _sanitizer.Sanitize(
                    check.SanitizedError),
                Limitation = _sanitizer.Sanitize(
                    check.Limitation)
            })
            .ToArray();

        return result with
        {
            ReferenceId = _sanitizer.Sanitize(
                result.ReferenceId),
            ApplicationVersion = _sanitizer.Sanitize(
                result.ApplicationVersion),
            BuildIdentifier = _sanitizer.Sanitize(
                result.BuildIdentifier),
            Environment = environment,
            Checks = checks,
            Issues = result.Issues
                .Select(_sanitizer.Sanitize)
                .ToArray()
        };
    }

    private void ApplyRetention(DateTimeOffset currentUtc)
    {
        if (!Directory.Exists(_recordsDirectory))
        {
            return;
        }

        try
        {
            var cutoff = currentUtc.UtcDateTime -
                _maximumRecordAge;
            var records = Directory
                .EnumerateFiles(
                    _recordsDirectory,
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
                    _recordsDirectory,
                    "*.json",
                    SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(
                    record => record.LastWriteTimeUtc)
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
