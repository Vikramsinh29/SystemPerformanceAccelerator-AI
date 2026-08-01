using System.IO.Compression;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Diagnostics;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class DiagnosticPackageExporterTests
{
    [Fact]
    public async Task CreatePreview_ReportsRecordedEventsAndHardwareChoice()
    {
        using var location = new TemporaryDiagnosticLocation();
        var service = CreateService(location.DiagnosticsRoot);
        service.Configure(
            enabled: true,
            includeHardwareSummary: true);

        var reference = await service.RecordExceptionAsync(
            new InvalidOperationException("Failure"),
            "Cleaner",
            "Scan",
            false,
            false);

        var preview = service.CreateExportPreview();

        Assert.Equal(1, preview.EventCount);
        Assert.Contains(reference!, preview.ErrorReferences);
        Assert.True(preview.IncludesHardwareSummary);
    }

    [Fact]
    public async Task ExportAsync_CreatesInspectableZipWithSanitizedEntries()
    {
        using var location = new TemporaryDiagnosticLocation();
        var sanitizer = new DiagnosticPathSanitizer(
            userProfile: @"C:\Users\Alice",
            userName: "Alice");
        var service = CreateService(
            location.DiagnosticsRoot,
            sanitizer);
        service.Configure(
            enabled: true,
            includeHardwareSummary: false);

        var reference = await service.RecordExceptionAsync(
            new InvalidOperationException(
                @"Failed at C:\Users\Alice\Documents\File.txt"),
            "Cleaner",
            "Scan",
            false,
            false);

        var result = await service.ExportAsync(
            location.ExportPath);

        Assert.True(result.Success);
        Assert.Equal(1, result.EventCount);
        Assert.True(File.Exists(location.ExportPath));

        using var archive = ZipFile.OpenRead(location.ExportPath);
        var names = archive.Entries
            .Select(entry => entry.FullName)
            .ToArray();

        Assert.Contains("README.txt", names);
        Assert.Contains("manifest.json", names);
        Assert.Contains("environment.json", names);
        Assert.Contains($"events/{reference}.json", names);

        var eventEntry = archive.GetEntry(
            $"events/{reference}.json");
        var savedEntry = Assert.IsType<ZipArchiveEntry>(eventEntry);
        using var reader = new StreamReader(savedEntry.Open());
        var eventJson = reader.ReadToEnd();

        Assert.Contains("%USERPROFILE%", eventJson);
        Assert.DoesNotContain(@"C:\Users\Alice", eventJson);
        Assert.DoesNotContain("Test CPU", eventJson);
    }

    [Fact]
    public async Task ExportAsync_WithNoEvents_StillCreatesSupportSummary()
    {
        using var location = new TemporaryDiagnosticLocation();
        var service = CreateService(location.DiagnosticsRoot);
        service.Configure(
            enabled: true,
            includeHardwareSummary: false);

        var result = await service.ExportAsync(
            location.ExportPath);

        Assert.True(result.Success);
        Assert.Equal(0, result.EventCount);

        using var archive = ZipFile.OpenRead(location.ExportPath);
        Assert.NotNull(archive.GetEntry("README.txt"));
        Assert.NotNull(archive.GetEntry("manifest.json"));
        Assert.NotNull(archive.GetEntry("environment.json"));
    }

    private static LocalDiagnosticService CreateService(
        string root,
        DiagnosticPathSanitizer? sanitizer = null) =>
        new(
            root,
            sanitizer,
            () => new DiagnosticEnvironment(
                "1.0.0",
                "test-build",
                "Windows test",
                ".NET test",
                true,
                2_000_000,
                5_000_000,
                "Test CPU",
                8_000_000));

    private sealed class TemporaryDiagnosticLocation :
        IDisposable
    {
        public TemporaryDiagnosticLocation()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                $"pc-spa-export-tests-{Guid.NewGuid():N}");
            DiagnosticsRoot = Path.Combine(
                Root,
                "diagnostics");
            ExportPath = Path.Combine(
                Root,
                "PC-SPA-Diagnostics.zip");
        }

        public string Root { get; }

        public string DiagnosticsRoot { get; }

        public string ExportPath { get; }

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
