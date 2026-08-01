using System.IO.Compression;
using System.Text.Json;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Repairs;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class WindowsRepairExecutionReportExporterTests
{
    [Fact]
    public async Task ExportAsync_CreatesOnlyRepairResultEntries()
    {
        using var location = new TemporaryExportLocation();
        var destination = Path.Combine(
            location.Root,
            "repair-result.zip");

        var exported = await new WindowsRepairExecutionReportExporter()
            .ExportAsync(CreateResult(), destination);

        Assert.Equal(destination, exported);
        using var archive = ZipFile.OpenRead(destination);
        var names = archive.Entries
            .Select(entry => entry.FullName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var expected = new[]
            {
                "README.txt",
                "manifest.json",
                "repair-result.json"
            }
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(expected, names);
        Assert.DoesNotContain("assessment.json", names);
    }

    [Fact]
    public async Task ExportAsync_ManifestIdentifiesRepairAndRestartEvidence()
    {
        using var location = new TemporaryExportLocation();
        var destination = Path.Combine(
            location.Root,
            "repair-result.zip");

        await new WindowsRepairExecutionReportExporter()
            .ExportAsync(CreateResult(), destination);

        using var archive = ZipFile.OpenRead(destination);
        var entry = archive.GetEntry("manifest.json");
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry.Open());
        using var document = JsonDocument.Parse(
            await reader.ReadToEndAsync());
        var root = document.RootElement;

        Assert.Equal(
            "Guided Windows Repair Result",
            root.GetProperty("ReportType").GetString());
        Assert.True(
            root.GetProperty("ContainsRepairActions").GetBoolean());
        Assert.False(
            root.GetProperty("AutomaticRestartAttempted").GetBoolean());
        Assert.False(
            root.GetProperty("ContainsPersonalFiles").GetBoolean());
        Assert.False(
            root.GetProperty("AutomaticUpload").GetBoolean());
    }

    internal static WindowsRepairExecutionResult CreateResult()
    {
        var started = DateTimeOffset.Parse(
            "2026-08-01T12:00:00Z");
        return new WindowsRepairExecutionResult(
            "REPAIR-EXPORT",
            "ASSESS-EXPORT",
            started,
            started.AddMinutes(4),
            "1.0.0",
            "test-build",
            WindowsRepairExecutionOutcome.Completed,
            "Completed.",
            Enum.GetValues<WindowsRepairExecutionStep>()
                .Select((step, index) =>
                    new WindowsRepairExecutionStepResult(
                        step,
                        WindowsRepairExecutionStepOutcome.Succeeded,
                        step.ToString(),
                        "Completed.",
                        ChangesWindows: index < 2,
                        index % 2 == 0 ? "DISM.exe" : "sfc.exe",
                        Array.Empty<string>(),
                        0,
                        started.AddMinutes(index),
                        started.AddMinutes(index + 1),
                        string.Empty,
                        string.Empty))
                .ToArray(),
            VerificationAssessment: null,
            StopRequested: false,
            AutomaticRestartAttempted: false,
            Issues: Array.Empty<string>());
    }

    private sealed class TemporaryExportLocation : IDisposable
    {
        public TemporaryExportLocation() =>
            Directory.CreateDirectory(Root);

        public string Root { get; } = Path.Combine(
            Path.GetTempPath(),
            $"pc-spa-repair-result-export-{Guid.NewGuid():N}");

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
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
