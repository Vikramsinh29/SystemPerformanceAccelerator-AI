using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Diagnostics;

public sealed class DiagnosticPackageExporter
{
    private const int MaximumSubmittedEventCount = 5;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _eventsDirectory;
    private readonly DiagnosticPathSanitizer _sanitizer;
    private readonly Func<DiagnosticEnvironment> _environmentProvider;

    public DiagnosticPackageExporter(
        string eventsDirectory,
        DiagnosticPathSanitizer sanitizer,
        Func<DiagnosticEnvironment> environmentProvider)
    {
        _eventsDirectory = Path.GetFullPath(
            eventsDirectory ?? throw new ArgumentNullException(
                nameof(eventsDirectory)));
        _sanitizer = sanitizer ?? throw new ArgumentNullException(
            nameof(sanitizer));
        _environmentProvider = environmentProvider ??
            throw new ArgumentNullException(nameof(environmentProvider));
    }

    public DiagnosticExportPreview CreatePreview(
        string diagnosticsRoot,
        bool includeHardwareSummary)
    {
        var events = ReadEvents();
        return new DiagnosticExportPreview(
            events.Count,
            events
                .OrderByDescending(item => item.TimestampUtc)
                .Select(item => item.ReferenceId)
                .ToArray(),
            includeHardwareSummary,
            diagnosticsRoot,
            "The package contains sanitized PC-SPA error records and a local environment summary. It does not contain document contents, browser history, passwords, cookies, email addresses, licence keys, machine serial numbers, or full personal paths.");
    }

    public async Task<DiagnosticExportResult> ExportAsync(
        string destinationZipPath,
        bool includeHardwareSummary,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationZipPath))
        {
            throw new ArgumentException(
                "A diagnostic export path is required.",
                nameof(destinationZipPath));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var fullDestinationPath = Path.GetFullPath(destinationZipPath);
        if (!string.Equals(
                Path.GetExtension(fullDestinationPath),
                ".zip",
                StringComparison.OrdinalIgnoreCase))
        {
            fullDestinationPath += ".zip";
        }

        var destinationDirectory = Path.GetDirectoryName(fullDestinationPath)
            ?? throw new InvalidOperationException(
                "The diagnostic export path has no parent directory.");
        Directory.CreateDirectory(destinationDirectory);

        var temporaryPath = fullDestinationPath + ".tmp";
        TryDelete(temporaryPath);

        var events = ReadEvents()
            .OrderBy(item => item.TimestampUtc)
            .ToArray();

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                4096,
                useAsync: true))
            using (var archive = new ZipArchive(
                stream,
                ZipArchiveMode.Create,
                leaveOpen: false))
            {
                await WriteTextEntryAsync(
                    archive,
                    "README.txt",
                    BuildReadme(events.Length, includeHardwareSummary),
                    cancellationToken);

                var environment = events.LastOrDefault()?.Environment ??
                    _environmentProvider();
                environment = FilterEnvironment(
                    environment,
                    includeHardwareSummary);
                await WriteJsonEntryAsync(
                    archive,
                    "environment.json",
                    environment,
                    cancellationToken);

                var manifest = new
                {
                    Product = "PC-SPA",
                    FormatVersion = 1,
                    CreatedUtc = DateTimeOffset.UtcNow,
                    EventCount = events.Length,
                    IncludesHardwareSummary = includeHardwareSummary,
                    Privacy = "Local-only manual export. Review the ZIP before sharing."
                };
                await WriteJsonEntryAsync(
                    archive,
                    "manifest.json",
                    manifest,
                    cancellationToken);

                foreach (var diagnosticEvent in events)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var sanitizedEvent = SanitizeEvent(
                        diagnosticEvent,
                        includeHardwareSummary);
                    await WriteJsonEntryAsync(
                        archive,
                        $"events/{sanitizedEvent.ReferenceId}.json",
                        sanitizedEvent,
                        cancellationToken);
                }
            }

            File.Move(
                temporaryPath,
                fullDestinationPath,
                overwrite: true);

            return new DiagnosticExportResult(
                true,
                fullDestinationPath,
                events.Length,
                events.Length == 0
                    ? "Diagnostic package exported with the local environment summary. No crash events were available."
                    : $"Diagnostic package exported with {events.Length:N0} sanitized event(s). Review the ZIP before sharing.");
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    public async Task<DiagnosticExportResult> ExportFeedbackAsync(
        string destinationZipPath,
        DiagnosticFeedbackRequest feedback,
        bool includeHardwareSummary,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        if (string.IsNullOrWhiteSpace(feedback.Description))
        {
            throw new ArgumentException(
                "A description of the error is required.",
                nameof(feedback));
        }

        var result = await ExportAsync(
            destinationZipPath,
            includeHardwareSummary && feedback.IncludeSanitizedDiagnostics,
            cancellationToken);

        using var archive = ZipFile.Open(
            result.ExportPath,
            ZipArchiveMode.Update);
        if (!feedback.IncludeSanitizedDiagnostics)
        {
            foreach (var entry in archive.Entries
                         .Where(entry => entry.FullName.StartsWith(
                             "events/",
                             StringComparison.Ordinal))
                         .ToArray())
            {
                entry.Delete();
            }

            archive.GetEntry("README.txt")?.Delete();
            archive.GetEntry("manifest.json")?.Delete();
            await WriteTextEntryAsync(
                archive,
                "README.txt",
                BuildReadme(0, includeHardwareSummary: false),
                cancellationToken);
            await WriteJsonEntryAsync(
                archive,
                "manifest.json",
                new
                {
                    Product = "PC-SPA",
                    FormatVersion = 1,
                    CreatedUtc = DateTimeOffset.UtcNow,
                    EventCount = 0,
                    IncludesHardwareSummary = false,
                    Privacy = "Local-only manual feedback package. Review the ZIP before sharing."
                },
                cancellationToken);
        }
        var sanitizedFeedback = feedback with
        {
            ErrorReference = _sanitizer.Sanitize(feedback.ErrorReference),
            AffectedArea = _sanitizer.Sanitize(feedback.AffectedArea),
            Description = _sanitizer.Sanitize(feedback.Description),
            ExpectedResult = _sanitizer.Sanitize(feedback.ExpectedResult)
        };
        await WriteJsonEntryAsync(
            archive,
            "feedback.json",
            new
            {
                FormatVersion = 1,
                CreatedUtc = DateTimeOffset.UtcNow,
                Feedback = sanitizedFeedback,
                Privacy = "User-reviewed local package. PC-SPA did not transmit it."
            },
            cancellationToken);

        return result with
        {
            EventCount = feedback.IncludeSanitizedDiagnostics
                ? result.EventCount
                : 0,
            Message = "Privacy-safe error feedback package created. PC-SPA did not send or upload it. Review the ZIP before sharing."
        };
    }

    public DiagnosticFeedbackSubmissionRequest CreateFeedbackSubmission(
        DiagnosticFeedbackRequest feedback,
        string? installationId)
    {
        ArgumentNullException.ThrowIfNull(feedback);

        var environment = _environmentProvider();
        var events = feedback.IncludeSanitizedDiagnostics
            ? ReadEvents()
                .OrderByDescending(item => item.TimestampUtc)
                .Take(MaximumSubmittedEventCount)
                .Select(item => new DiagnosticFeedbackEvent(
                    Limit(_sanitizer.Sanitize(item.ReferenceId), 64),
                    Limit(_sanitizer.Sanitize(item.ExceptionType), 160),
                    RequiredText(
                        _sanitizer.Sanitize(item.Message),
                        "No diagnostic message recorded.",
                        2000),
                    Limit(_sanitizer.Sanitize(item.StackTrace), 4000)))
                .ToArray()
            : [];

        return new DiagnosticFeedbackSubmissionRequest(
            1,
            Limit(_sanitizer.Sanitize(environment.ApplicationVersion), 40),
            Limit(_sanitizer.Sanitize(environment.BuildIdentifier), 100),
            Limit(_sanitizer.Sanitize(feedback.ErrorReference), 64),
            Limit(_sanitizer.Sanitize(feedback.AffectedArea), 80),
            Limit(_sanitizer.Sanitize(feedback.Description), 2000),
            Limit(_sanitizer.Sanitize(feedback.ExpectedResult), 1000),
            Limit(_sanitizer.Sanitize(environment.WindowsVersion), 160),
            Limit(_sanitizer.Sanitize(environment.RuntimeVersion), 100),
            environment.IsElevated,
            NormalizeInstallationId(installationId),
            events);
    }

    private IReadOnlyList<DiagnosticEvent> ReadEvents()
    {
        if (!Directory.Exists(_eventsDirectory))
        {
            return [];
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(
                _eventsDirectory,
                "*.json",
                SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            return [];
        }

        var events = new List<DiagnosticEvent>();
        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var diagnosticEvent =
                    JsonSerializer.Deserialize<DiagnosticEvent>(
                        json,
                        SerializerOptions);
                if (diagnosticEvent is not null &&
                    diagnosticEvent.Environment is not null &&
                    IsValidReferenceId(
                        diagnosticEvent.ReferenceId))
                {
                    events.Add(diagnosticEvent);
                }
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException or
                JsonException or
                NotSupportedException or
                System.Security.SecurityException)
            {
            }
        }

        return events;
    }

    private DiagnosticEvent SanitizeEvent(
        DiagnosticEvent diagnosticEvent,
        bool includeHardwareSummary) =>
        diagnosticEvent with
        {
            InstallationId = NormalizeInstallationId(
                diagnosticEvent.InstallationId),
            Feature = _sanitizer.Sanitize(diagnosticEvent.Feature),
            OperationStage = _sanitizer.Sanitize(
                diagnosticEvent.OperationStage),
            ExceptionType = _sanitizer.Sanitize(
                diagnosticEvent.ExceptionType),
            Message = _sanitizer.Sanitize(diagnosticEvent.Message),
            StackTrace = _sanitizer.Sanitize(
                diagnosticEvent.StackTrace),
            Environment = FilterEnvironment(
                diagnosticEvent.Environment,
                includeHardwareSummary)
        };

    private DiagnosticEnvironment FilterEnvironment(
        DiagnosticEnvironment environment,
        bool includeHardwareSummary) =>
        environment with
        {
            ApplicationVersion = _sanitizer.Sanitize(
                environment.ApplicationVersion),
            BuildIdentifier = _sanitizer.Sanitize(
                environment.BuildIdentifier),
            WindowsVersion = _sanitizer.Sanitize(
                environment.WindowsVersion),
            RuntimeVersion = _sanitizer.Sanitize(
                environment.RuntimeVersion),
            CpuModel = includeHardwareSummary
                ? _sanitizer.Sanitize(environment.CpuModel)
                : null,
            InstalledMemoryBytes = includeHardwareSummary
                ? environment.InstalledMemoryBytes
                : null
        };


    private static bool IsValidReferenceId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length is >= 12 and <= 64 &&
        value.StartsWith("ERR-", StringComparison.Ordinal) &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) ||
            character == '-');

    private static string NormalizeInstallationId(string? value) =>
        Guid.TryParseExact(value, "N", out var identity)
            ? identity.ToString("N")
            : "invalid-installation-id";

    private static string Limit(string value, int maximumLength) =>
        value.Length <= maximumLength
            ? value
            : value[..maximumLength];

    private static string RequiredText(
        string value,
        string fallback,
        int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? fallback
            : Limit(value, maximumLength);

    private static async Task WriteJsonEntryAsync<T>(
        ZipArchive archive,
        string entryName,
        T value,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(
            entryName,
            CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await JsonSerializer.SerializeAsync(
            entryStream,
            value,
            SerializerOptions,
            cancellationToken);
    }

    private static async Task WriteTextEntryAsync(
        ZipArchive archive,
        string entryName,
        string value,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = archive.CreateEntry(
            entryName,
            CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await using var writer = new StreamWriter(
            entryStream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(value);
        await writer.FlushAsync(cancellationToken);
    }

    private static string BuildReadme(
        int eventCount,
        bool includeHardwareSummary) =>
        $"""
        PC-SPA local diagnostic package

        Event count: {eventCount}
        Hardware summary included: {(includeHardwareSummary ? "Yes" : "No")}

        This ZIP was created only after a user-requested manual export.
        It contains sanitized diagnostic records and environment information.
        PC-SPA does not upload this package or transmit it automatically.

        Before sharing:
        1. Open and inspect this ZIP.
        2. Confirm that its contents are appropriate for the intended support recipient.
        3. Share it only through a channel you trust.

        Excluded by design:
        - document contents
        - browser history
        - passwords and cookies
        - email addresses
        - licence keys
        - machine serial numbers
        - unrelated process command lines
        - full personal paths
        """;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException)
        {
        }
    }
}
