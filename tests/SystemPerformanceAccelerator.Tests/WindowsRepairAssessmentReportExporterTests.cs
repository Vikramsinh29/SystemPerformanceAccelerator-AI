using System.IO.Compression;
using System.Text.Json;
using SystemPerformanceAccelerator.Infrastructure.Repairs;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class WindowsRepairAssessmentReportExporterTests
{
    [Fact]
    public async Task ExportAsync_CreatesOnlyExpectedEntries()
    {
        using var location = new TemporaryExportLocation();
        var exporter =
            new WindowsRepairAssessmentReportExporter();
        var destination = Path.Combine(
            location.Root,
            "assessment.zip");

        var result = await exporter.ExportAsync(
            WindowsRepairAssessmentHistoryServiceTests
                .CreateResult(
                    "ASSESS-20260801000000-EEEE"),
            destination);

        Assert.Equal(destination, result);
        using var archive = ZipFile.OpenRead(destination);
        var names = archive.Entries
            .Select(entry => entry.FullName)
            .OrderBy(
                name => name,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var expectedNames = new[]
            {
                "README.txt",
                "assessment.json",
                "manifest.json"
            }
            .OrderBy(
                name => name,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(expectedNames, names);
    }

    [Fact]
    public async Task ExportAsync_ManifestStatesNoRepairOrAutomaticUpload()
    {
        using var location = new TemporaryExportLocation();
        var exporter =
            new WindowsRepairAssessmentReportExporter();
        var destination = Path.Combine(
            location.Root,
            "assessment.zip");

        await exporter.ExportAsync(
            WindowsRepairAssessmentHistoryServiceTests
                .CreateResult(
                    "ASSESS-20260801000000-FFFF"),
            destination);

        using var archive = ZipFile.OpenRead(destination);
        var entry = archive.GetEntry("manifest.json");
        Assert.NotNull(entry);

        using var reader = new StreamReader(entry.Open());
        var json = await reader.ReadToEndAsync();
        using var document = JsonDocument.Parse(json);

        Assert.False(
            document.RootElement
                .GetProperty("ContainsRepairActions")
                .GetBoolean());
        Assert.False(
            document.RootElement
                .GetProperty("ContainsPersonalFiles")
                .GetBoolean());
        Assert.False(
            document.RootElement
                .GetProperty("AutomaticUpload")
                .GetBoolean());
    }

    private sealed class TemporaryExportLocation :
        IDisposable
    {
        public TemporaryExportLocation()
        {
            Directory.CreateDirectory(Root);
        }

        public string Root { get; } = Path.Combine(
            Path.GetTempPath(),
            $"pc-spa-repair-export-{Guid.NewGuid():N}");

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
