using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Services;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class HealthCheckServiceTests
{
    private const long Gigabyte = 1024L * 1024 * 1024;

    [Fact]
    public async Task RunAsync_CombinesDriveCpuMemoryAndStartupChecks()
    {
        var service = CreateService(
            new SystemMonitorSnapshot(
                DateTimeOffset.Now,
                CpuUsagePercent: 25,
                TotalPhysicalMemoryBytes: 16 * Gigabyte,
                AvailablePhysicalMemoryBytes: 8 * Gigabyte),
            new StartupItemScanResult(
                [CreateStartupItem(StartupTargetState.Available)],
                [],
                LocationsScanned: 6,
                Elapsed: TimeSpan.Zero),
            new SystemDriveSpace(
                "C:\\",
                500 * Gigabyte,
                200 * Gigabyte));

        var result = await service.RunAsync();

        Assert.Equal(4, result.Items.Count);
        Assert.Equal(4, result.Recommendations.Count);
        Assert.All(result.Items, item =>
            Assert.Equal(HealthCheckStatus.Good, item.Status));
        Assert.All(result.Recommendations, recommendation =>
            Assert.Equal(
                HealthRecommendationPriority.Low,
                recommendation.Priority));
        Assert.Equal(HealthCheckStatus.Good, result.OverallStatus);
        Assert.Equal(4, result.GoodCount);
        Assert.Equal(0, result.HighPriorityRecommendationCount);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task RunAsync_FlagsConfirmedAttentionConditions()
    {
        var service = CreateService(
            new SystemMonitorSnapshot(
                DateTimeOffset.Now,
                CpuUsagePercent: 95,
                TotalPhysicalMemoryBytes: 16 * Gigabyte,
                AvailablePhysicalMemoryBytes: Gigabyte),
            new StartupItemScanResult(
                [CreateStartupItem(StartupTargetState.Missing)],
                [],
                LocationsScanned: 6,
                Elapsed: TimeSpan.Zero),
            new SystemDriveSpace(
                "C:\\",
                100 * Gigabyte,
                5 * Gigabyte));

        var result = await service.RunAsync();

        Assert.Equal(4, result.AttentionCount);
        Assert.Equal(HealthCheckStatus.Attention, result.OverallStatus);
        Assert.All(result.Items, item =>
            Assert.Equal(HealthCheckStatus.Attention, item.Status));
        Assert.Equal(4, result.Recommendations.Count);
        Assert.Equal(2, result.HighPriorityRecommendationCount);
    }

    [Fact]
    public async Task RunAsync_ReportsUnavailableSourcesAsUnknownWithoutCrashing()
    {
        var monitor = new FakeSystemMonitorService(
            _ => throw new InvalidOperationException("monitor unavailable"));
        var startup = new FakeStartupItemService(
            new StartupItemScanResult(
                [],
                ["one registry location was inaccessible"],
                LocationsScanned: 5,
                Elapsed: TimeSpan.Zero));
        var service = new HealthCheckService(
            monitor,
            startup,
            () => throw new IOException("drive unavailable"));

        var result = await service.RunAsync();

        Assert.Equal(4, result.UnknownCount);
        Assert.Equal(HealthCheckStatus.Unknown, result.OverallStatus);
        Assert.Equal(3, result.Errors.Count);
        Assert.Equal(4, result.Recommendations.Count);
        Assert.All(result.Recommendations, recommendation =>
            Assert.Equal(
                HealthRecommendationPriority.Medium,
                recommendation.Priority));
        Assert.Contains(result.Items, item =>
            item.Name == "System drive" && item.Value == "Unavailable");
        Assert.Contains(result.Items, item =>
            item.Name == "Startup inventory" &&
            item.Status == HealthCheckStatus.Unknown);
    }

    [Fact]
    public async Task RunAsync_HonorsCancellationBeforeReadingSources()
    {
        var providerCalled = false;
        var service = new HealthCheckService(
            new FakeSystemMonitorService(_ => CreateNormalSnapshot()),
            new FakeStartupItemService(CreateNormalStartupResult()),
            () =>
            {
                providerCalled = true;
                return new SystemDriveSpace(
                    "C:\\",
                    100 * Gigabyte,
                    50 * Gigabyte);
            });
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.RunAsync(cancellationTokenSource.Token));

        Assert.False(providerCalled);
    }

    [Fact]
    public async Task RunAsync_LinksOneRecommendationToEveryFinding()
    {
        var service = CreateService(
            CreateNormalSnapshot(),
            CreateNormalStartupResult(),
            new SystemDriveSpace(
                "C:\\",
                500 * Gigabyte,
                250 * Gigabyte));

        var result = await service.RunAsync();

        var findingAreas = result.Items
            .Select(item => item.Name)
            .OrderBy(name => name)
            .ToArray();
        var recommendationAreas = result.Recommendations
            .Select(item => item.Area)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(findingAreas, recommendationAreas);
        Assert.All(result.Recommendations, recommendation =>
        {
            Assert.False(string.IsNullOrWhiteSpace(recommendation.Title));
            Assert.False(string.IsNullOrWhiteSpace(recommendation.Recommendation));
            Assert.False(string.IsNullOrWhiteSpace(recommendation.WhyItMatters));
        });
    }

    [Fact]
    public async Task RunAsync_AssignsPrioritiesToAttentionRecommendations()
    {
        var service = CreateService(
            new SystemMonitorSnapshot(
                DateTimeOffset.Now,
                CpuUsagePercent: 95,
                TotalPhysicalMemoryBytes: 16 * Gigabyte,
                AvailablePhysicalMemoryBytes: Gigabyte),
            new StartupItemScanResult(
                [CreateStartupItem(StartupTargetState.Malformed)],
                [],
                LocationsScanned: 6,
                Elapsed: TimeSpan.Zero),
            new SystemDriveSpace(
                "C:\\",
                100 * Gigabyte,
                5 * Gigabyte));

        var result = await service.RunAsync();

        Assert.Equal(
            HealthRecommendationPriority.High,
            GetRecommendation(result, "System drive").Priority);
        Assert.Equal(
            HealthRecommendationPriority.Medium,
            GetRecommendation(result, "Current CPU usage").Priority);
        Assert.Equal(
            HealthRecommendationPriority.High,
            GetRecommendation(result, "Physical memory").Priority);
        Assert.Equal(
            HealthRecommendationPriority.Medium,
            GetRecommendation(result, "Startup inventory").Priority);
    }

    [Fact]
    public async Task RunAsync_UnknownRecommendationsRemainReadOnlyGuidance()
    {
        var monitor = new FakeSystemMonitorService(
            _ => throw new InvalidOperationException("monitor unavailable"));
        var startup = new FakeStartupItemService(
            new StartupItemScanResult(
                [],
                ["startup source unavailable"],
                LocationsScanned: 2,
                Elapsed: TimeSpan.Zero));
        var service = new HealthCheckService(
            monitor,
            startup,
            () => throw new IOException("drive unavailable"));

        var result = await service.RunAsync();

        Assert.All(result.Recommendations, recommendation =>
        {
            Assert.Equal(
                HealthRecommendationPriority.Medium,
                recommendation.Priority);
            Assert.True(
                recommendation.Recommendation.Contains("Run Health Check again") ||
                recommendation.Recommendation.Contains("Open Startup Manager"));
        });
    }

    private static HealthRecommendation GetRecommendation(
        HealthCheckResult result,
        string area) =>
        Assert.Single(result.Recommendations, item => item.Area == area);

    private static HealthCheckService CreateService(
        SystemMonitorSnapshot snapshot,
        StartupItemScanResult startupResult,
        SystemDriveSpace driveSpace) =>
        new(
            new FakeSystemMonitorService(_ => snapshot),
            new FakeStartupItemService(startupResult),
            () => driveSpace);

    private static SystemMonitorSnapshot CreateNormalSnapshot() =>
        new(
            DateTimeOffset.Now,
            CpuUsagePercent: 20,
            TotalPhysicalMemoryBytes: 16 * Gigabyte,
            AvailablePhysicalMemoryBytes: 8 * Gigabyte);

    private static StartupItemScanResult CreateNormalStartupResult() =>
        new(
            [CreateStartupItem(StartupTargetState.Available)],
            [],
            LocationsScanned: 6,
            Elapsed: TimeSpan.Zero);

    private static StartupItem CreateStartupItem(
        StartupTargetState targetState) =>
        new(
            "Test startup item",
            "test.exe",
            "Test source",
            "Test location",
            StartupItemState.Enabled,
            targetState);

    private sealed class FakeSystemMonitorService(
        Func<CancellationToken, SystemMonitorSnapshot> capture)
        : ISystemMonitorService
    {
        public Task<SystemMonitorSnapshot> CaptureAsync(
            TimeSpan sampleDuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(capture(cancellationToken));
        }
    }

    private sealed class FakeStartupItemService(StartupItemScanResult result)
        : IStartupItemService
    {
        public Task<StartupItemScanResult> ScanAsync(
            IProgress<StartupItemScanProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }

        public Task<StartupItemStateChangeResult> SetStateAsync(
            StartupItem item,
            StartupItemState requestedState,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new StartupItemStateChangeResult(
                StartupItemStateChangeOutcome.Unsupported,
                requestedState,
                "Health-check tests do not modify startup items."));
        }
    }
}
