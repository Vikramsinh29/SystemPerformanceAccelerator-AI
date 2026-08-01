using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Repairs;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class WindowsRepairExecutionHistoryServiceTests
{
    [Fact]
    public async Task SaveAndLoad_SanitizesPersonalPath()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var service =
                new WindowsRepairExecutionHistoryService(
                    root);
            var result = CreateResult(
                "REPAIR-ONE",
                @"C:\Users\Alice\secret.txt");

            await service.SaveAsync(result);
            var loaded = service.LoadLatest();

            Assert.NotNull(loaded);
            Assert.DoesNotContain(
                "Alice",
                loaded.Steps[0].SanitizedOutput,
                StringComparison.OrdinalIgnoreCase);
            Assert.False(
                loaded.AutomaticRestartAttempted);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Retention_KeepsConfiguredMaximum()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var now = DateTimeOffset.Parse(
                "2026-08-01T00:00:00Z");
            var service =
                new WindowsRepairExecutionHistoryService(
                    root,
                    utcNow: () => now,
                    maximumRecordCount: 1);

            await service.SaveAsync(
                CreateResult(
                    "REPAIR-ONE",
                    "first"));
            await Task.Delay(20);
            await service.SaveAsync(
                CreateResult(
                    "REPAIR-TWO",
                    "second"));

            Assert.Single(
                Directory.EnumerateFiles(
                    root,
                    "*.json"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task LoadRecent_ReturnsNewestRequestedRecords()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var service =
                new WindowsRepairExecutionHistoryService(root);
            await service.SaveAsync(
                CreateResult("REPAIR-RECENT-ONE", "first"));
            await Task.Delay(20);
            await service.SaveAsync(
                CreateResult("REPAIR-RECENT-TWO", "second"));

            var records = service.LoadRecent(1);

            Assert.Single(records);
            Assert.Equal(
                "REPAIR-RECENT-TWO",
                records[0].ReferenceId);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task ExportLatestAsync_UsesLatestSanitizedResult()
    {
        var root = CreateTemporaryDirectory();
        var exportRoot = CreateTemporaryDirectory();
        try
        {
            var service =
                new WindowsRepairExecutionHistoryService(root);
            await service.SaveAsync(
                CreateResult(
                    "REPAIR-EXPORT",
                    @"C:\Users\Alice\secret.txt"));
            var destination = Path.Combine(
                exportRoot,
                "repair-result.zip");

            var exported = await service.ExportLatestAsync(
                destination);

            Assert.Equal(destination, exported);
            using var archive =
                System.IO.Compression.ZipFile.OpenRead(destination);
            var entry = archive.GetEntry("repair-result.json");
            Assert.NotNull(entry);
            using var reader = new StreamReader(entry.Open());
            var json = await reader.ReadToEndAsync();
            Assert.DoesNotContain(
                "Alice",
                json,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
            TryDelete(exportRoot);
        }
    }

    private static WindowsRepairExecutionResult
        CreateResult(
            string reference,
            string output)
    {
        var now = DateTimeOffset.Parse(
            "2026-08-01T00:00:00Z");

        return new WindowsRepairExecutionResult(
            reference,
            "ASSESS-TEST",
            now,
            now.AddSeconds(1),
            "1.0.0",
            "test-build",
            WindowsRepairExecutionOutcome.Completed,
            "Completed.",
            new[]
            {
                new WindowsRepairExecutionStepResult(
                    WindowsRepairExecutionStep
                        .ComponentStoreRepair,
                    WindowsRepairExecutionStepOutcome
                        .Succeeded,
                    "DISM RestoreHealth",
                    "Completed.",
                    ChangesWindows: true,
                    "DISM.exe",
                    new[] { "/RestoreHealth" },
                    0,
                    now,
                    now.AddSeconds(1),
                    output,
                    string.Empty)
            },
            VerificationAssessment: null,
            StopRequested: false,
            AutomaticRestartAttempted: false,
            Issues: Array.Empty<string>());
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "PC-SPA-Repair-History-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(
                    path,
                    recursive: true);
            }
        }
        catch
        {
        }
    }
}
