using System.Text.Json;
using System.Text.Json.Serialization;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Diagnostics;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class LocalDiagnosticServiceTests
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new()
        {
            Converters = { new JsonStringEnumConverter() }
        };

    [Fact]
    public async Task RecordExceptionAsync_WhenDisabled_WritesNothing()
    {
        using var location = new TemporaryDiagnosticLocation();
        var service = CreateService(location.Root);
        service.Configure(
            enabled: false,
            includeHardwareSummary: false);

        var reference = await service.RecordExceptionAsync(
            new InvalidOperationException("Test failure"),
            "Cleaner",
            "Scan",
            recovered: false,
            userDataMayHaveBeenAffected: false);

        Assert.Null(reference);
        Assert.False(Directory.Exists(location.EventsPath));
        Assert.Null(service.InstallationId);
    }

    [Fact]
    public async Task RecordExceptionAsync_WhenEnabled_WritesSanitizedEvent()
    {
        using var location = new TemporaryDiagnosticLocation();
        var sanitizer = new DiagnosticPathSanitizer(
            userProfile: @"C:\Users\Alice",
            userName: "Alice");
        var service = CreateService(
            location.Root,
            sanitizer: sanitizer);
        service.Configure(
            enabled: true,
            includeHardwareSummary: false);

        var reference = await service.RecordExceptionAsync(
            new InvalidOperationException(
                @"Failed at C:\Users\Alice\Documents\File.txt for alice@example.com."),
            "Cleaner",
            "Scan",
            recovered: false,
            userDataMayHaveBeenAffected: false);

        var savedReference = Assert.IsType<string>(reference);
        var eventPath = Path.Combine(
            location.EventsPath,
            savedReference + ".json");
        Assert.True(File.Exists(eventPath));

        var diagnosticEvent =
            JsonSerializer.Deserialize<DiagnosticEvent>(
                File.ReadAllText(eventPath),
                SerializerOptions);

        var savedEvent = Assert.IsType<DiagnosticEvent>(
            diagnosticEvent);
        Assert.Contains(
            "%USERPROFILE%",
            savedEvent.Message);
        Assert.Contains(
            "<redacted-email>",
            savedEvent.Message);
        Assert.DoesNotContain(
            "Alice",
            savedEvent.Message);
        Assert.Equal(
            service.InstallationId,
            savedEvent.InstallationId);
    }

    [Fact]
    public async Task RecordExceptionAsync_AppliesMaximumCountRetention()
    {
        using var location = new TemporaryDiagnosticLocation();
        var service = CreateService(
            location.Root,
            maximumEventCount: 2);
        service.Configure(
            enabled: true,
            includeHardwareSummary: false);

        await service.RecordExceptionAsync(
            new InvalidOperationException("One"),
            "Test",
            "One",
            false,
            false);
        await Task.Delay(20);
        await service.RecordExceptionAsync(
            new InvalidOperationException("Two"),
            "Test",
            "Two",
            false,
            false);
        await Task.Delay(20);
        await service.RecordExceptionAsync(
            new InvalidOperationException("Three"),
            "Test",
            "Three",
            false,
            false);

        Assert.Equal(
            2,
            Directory.GetFiles(
                location.EventsPath,
                "*.json").Length);
    }

    [Fact]
    public async Task RecordExceptionAsync_RemovesExpiredEvents()
    {
        using var location = new TemporaryDiagnosticLocation();
        var currentUtc = new DateTimeOffset(
            2026,
            8,
            1,
            10,
            0,
            0,
            TimeSpan.Zero);
        var service = CreateService(
            location.Root,
            utcNow: () => currentUtc,
            maximumEventAge: TimeSpan.FromDays(1));
        service.Configure(
            enabled: true,
            includeHardwareSummary: false);

        var oldReference = await service.RecordExceptionAsync(
            new InvalidOperationException("Old"),
            "Test",
            "Old",
            false,
            false);
        var oldPath = Path.Combine(
            location.EventsPath,
            Assert.IsType<string>(oldReference) + ".json");
        File.SetLastWriteTimeUtc(
            oldPath,
            currentUtc.UtcDateTime.AddDays(-2));

        currentUtc = currentUtc.AddHours(1);
        await service.RecordExceptionAsync(
            new InvalidOperationException("Current"),
            "Test",
            "Current",
            false,
            false);

        Assert.False(File.Exists(oldPath));
        Assert.Single(
            Directory.GetFiles(
                location.EventsPath,
                "*.json"));
    }

    [Fact]
    public async Task DeleteHistory_RemovesEventsButRetainsIdentity()
    {
        using var location = new TemporaryDiagnosticLocation();
        var service = CreateService(location.Root);
        service.Configure(
            enabled: true,
            includeHardwareSummary: false);
        var identity = service.InstallationId;

        await service.RecordExceptionAsync(
            new InvalidOperationException("Failure"),
            "Test",
            "Run",
            false,
            false);

        service.DeleteHistory();

        Assert.False(Directory.Exists(location.EventsPath));
        Assert.Equal(identity, service.InstallationId);
    }

    [Fact]
    public async Task ResetInstallationId_DeletesHistoryAndCreatesNewIdentity()
    {
        using var location = new TemporaryDiagnosticLocation();
        var service = CreateService(location.Root);
        service.Configure(
            enabled: true,
            includeHardwareSummary: false);
        var firstIdentity = service.InstallationId;

        await service.RecordExceptionAsync(
            new InvalidOperationException("Failure"),
            "Test",
            "Run",
            false,
            false);

        service.ResetInstallationId();

        Assert.NotEqual(firstIdentity, service.InstallationId);
        Assert.False(Directory.Exists(location.EventsPath));
    }

    [Fact]
    public void CreateExportPreview_IgnoresCorruptedEventFile()
    {
        using var location = new TemporaryDiagnosticLocation();
        var service = CreateService(location.Root);
        service.Configure(
            enabled: true,
            includeHardwareSummary: false);
        Directory.CreateDirectory(location.EventsPath);
        File.WriteAllText(
            Path.Combine(location.EventsPath, "broken.json"),
            "{ invalid json");

        var preview = service.CreateExportPreview();

        Assert.Equal(0, preview.EventCount);
        Assert.Empty(preview.ErrorReferences);
    }

    private static LocalDiagnosticService CreateService(
        string root,
        DiagnosticPathSanitizer? sanitizer = null,
        Func<DateTimeOffset>? utcNow = null,
        int maximumEventCount = 50,
        TimeSpan? maximumEventAge = null) =>
        new(
            root,
            sanitizer,
            CreateEnvironment,
            utcNow ?? (() => DateTimeOffset.UtcNow),
            maximumEventCount,
            maximumEventAge ?? TimeSpan.FromDays(30));

    private static DiagnosticEnvironment CreateEnvironment() =>
        new(
            "1.0.0",
            "test-build",
            "Windows test",
            ".NET test",
            true,
            2_000_000,
            5_000_000,
            "Test CPU",
            8_000_000);

    private sealed class TemporaryDiagnosticLocation :
        IDisposable
    {
        public TemporaryDiagnosticLocation()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                $"pc-spa-diagnostic-tests-{Guid.NewGuid():N}");
        }

        public string Root { get; }

        public string EventsPath =>
            Path.Combine(Root, "events");

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, true);
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
