using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Repairs;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class WindowsRepairAssessmentHistoryServiceTests
{
    [Fact]
    public async Task SaveAndLoadLatest_RoundTripsAssessment()
    {
        using var location = new TemporaryAssessmentLocation();
        var service = new WindowsRepairAssessmentHistoryService(
            location.Root);

        var expected = CreateResult("ASSESS-20260801000000-AAAA");
        await service.SaveAsync(expected);

        var actual = service.LoadLatest();

        Assert.NotNull(actual);
        Assert.Equal(expected.ReferenceId, actual.ReferenceId);
        Assert.Single(actual.Checks);
        Assert.False(
            actual.Checks[0].SanitizedOutput.Contains(
                Environment.UserName,
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LoadLatest_SkipsCorruptedNewestRecord()
    {
        using var location = new TemporaryAssessmentLocation();
        var service = new WindowsRepairAssessmentHistoryService(
            location.Root);

        var valid = CreateResult(
            "ASSESS-20260801000000-BBBB");
        await service.SaveAsync(valid);

        var records = Path.Combine(location.Root, "records");
        var corrupted = Path.Combine(
            records,
            "ASSESS-20260802000000-CCCC.json");
        File.WriteAllText(corrupted, "{ invalid json");
        File.SetLastWriteTimeUtc(
            corrupted,
            DateTime.UtcNow.AddMinutes(1));

        var actual = service.LoadLatest();

        Assert.NotNull(actual);
        Assert.Equal(valid.ReferenceId, actual.ReferenceId);
    }

    [Fact]
    public async Task SaveAsync_AppliesMaximumRecordCount()
    {
        using var location = new TemporaryAssessmentLocation();
        var now = DateTimeOffset.Parse(
            "2026-08-01T00:00:00Z");
        var service = new WindowsRepairAssessmentHistoryService(
            location.Root,
            utcNow: () => now,
            maximumRecordCount: 2);

        await service.SaveAsync(
            CreateResult("ASSESS-20260801000000-0001"));
        await service.SaveAsync(
            CreateResult("ASSESS-20260801000000-0002"));
        await service.SaveAsync(
            CreateResult("ASSESS-20260801000000-0003"));

        var files = Directory.GetFiles(
            Path.Combine(location.Root, "records"),
            "*.json");

        Assert.Equal(2, files.Length);
    }

    [Fact]
    public async Task LoadRecent_ReturnsNewestRequestedRecords()
    {
        using var location = new TemporaryAssessmentLocation();
        var service = new WindowsRepairAssessmentHistoryService(
            location.Root);
        await service.SaveAsync(
            CreateResult("ASSESS-RECENT-ONE"));
        await Task.Delay(20);
        await service.SaveAsync(
            CreateResult("ASSESS-RECENT-TWO"));

        var records = service.LoadRecent(1);

        Assert.Single(records);
        Assert.Equal(
            "ASSESS-RECENT-TWO",
            records[0].ReferenceId);
    }

    [Fact]
    public async Task SaveAsync_RemovesExpiredRecords()
    {
        using var location = new TemporaryAssessmentLocation();
        var now = DateTimeOffset.Parse(
            "2026-08-01T00:00:00Z");
        var service = new WindowsRepairAssessmentHistoryService(
            location.Root,
            utcNow: () => now,
            maximumRecordAge: TimeSpan.FromDays(30));

        await service.SaveAsync(
            CreateResult("ASSESS-20260801000000-OLD1"));

        var oldPath = Directory.GetFiles(
            Path.Combine(location.Root, "records"),
            "*.json").Single();
        File.SetLastWriteTimeUtc(
            oldPath,
            now.UtcDateTime.AddDays(-31));

        await service.SaveAsync(
            CreateResult("ASSESS-20260801000000-NEW1"));

        var files = Directory.GetFiles(
            Path.Combine(location.Root, "records"),
            "*.json");

        Assert.Single(files);
        Assert.Contains(
            "NEW1",
            Path.GetFileName(files[0]) ?? string.Empty);
    }

    [Fact]
    public async Task DeleteHistory_RemovesLocalAssessmentRoot()
    {
        using var location = new TemporaryAssessmentLocation();
        var service = new WindowsRepairAssessmentHistoryService(
            location.Root);
        await service.SaveAsync(
            CreateResult("ASSESS-20260801000000-DDDD"));

        service.DeleteHistory();

        Assert.False(Directory.Exists(location.Root));
        Assert.Null(service.LoadLatest());
    }

    internal static WindowsRepairAssessmentResult CreateResult(
        string referenceId)
    {
        var start = DateTimeOffset.Parse(
            "2026-08-01T00:00:00Z");
        var environment =
            new WindowsRepairEnvironmentStatus(
                true,
                true,
                "Windows",
                @"C:\Windows",
                @"C:\",
                true,
                true,
                10L * 1024 * 1024 * 1024,
                Array.Empty<string>());
        var check = new WindowsRepairCheckResult(
            WindowsRepairAssessmentCheck.ComponentStoreCheckHealth,
            WindowsRepairAssessmentOutcome.Healthy,
            "Windows component store",
            "No classified issue.",
            "DISM.exe",
            ["/Online", "/English", "/Cleanup-Image", "/CheckHealth"],
            0,
            start,
            start.AddSeconds(1),
            $@"C:\Users\{Environment.UserName}\private.txt",
            string.Empty,
            false,
            "Read-only.");

        return new WindowsRepairAssessmentResult(
            referenceId,
            start,
            start.AddSeconds(1),
            "1.0.0",
            "test-build",
            environment,
            [check],
            WindowsRepairAssessmentOutcome.Healthy,
            false,
            Array.Empty<string>());
    }

    private sealed class TemporaryAssessmentLocation :
        IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(),
            $"pc-spa-repair-history-{Guid.NewGuid():N}");

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
