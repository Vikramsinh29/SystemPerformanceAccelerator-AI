using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Services;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class CustomCleanServiceTests
{
    [Fact]
    public async Task PreviewAsync_SelectedTemporaryFiles_ReturnsMappedItemsAndErrors()
    {
        var candidate = new CleanupCandidate(
            Path.Combine(Path.GetTempPath(), "custom-clean.tmp"),
            2048,
            DateTime.UtcNow);
        var temporaryFiles = new FakeTemporaryFileService(
            new ScanResult(
                [candidate],
                ["one item was inaccessible"],
                TimeSpan.FromMilliseconds(10)));
        var service = new CustomCleanService(temporaryFiles);

        var result = await service.PreviewAsync([
            CustomCleanCategory.TemporaryFiles
        ]);

        var item = Assert.Single(result.Items);
        Assert.Equal(CustomCleanCategory.TemporaryFiles, item.Category);
        Assert.Equal(candidate.FullPath, item.FullPath);
        Assert.Equal(candidate.SizeBytes, item.SizeBytes);
        Assert.Equal(candidate.LastWriteTimeUtc, item.LastWriteTimeUtc);
        Assert.Equal(candidate.SizeBytes, result.TotalBytes);
        Assert.Single(result.Errors);
        Assert.Equal(1, temporaryFiles.ScanCallCount);
        Assert.Equal(0, temporaryFiles.CleanCallCount);
    }

    [Fact]
    public async Task PreviewAsync_NoSelectedCategories_ReturnsEmptyWithoutScanning()
    {
        var temporaryFiles = new FakeTemporaryFileService(
            new ScanResult([], [], TimeSpan.Zero));
        var service = new CustomCleanService(temporaryFiles);

        var result = await service.PreviewAsync([]);

        Assert.Empty(result.Items);
        Assert.Empty(result.Errors);
        Assert.Equal(0, result.TotalBytes);
        Assert.Equal(0, temporaryFiles.ScanCallCount);
        Assert.Equal(0, temporaryFiles.CleanCallCount);
    }

    [Fact]
    public async Task PreviewAsync_DuplicateCategory_ScansUnderlyingSourceOnlyOnce()
    {
        var temporaryFiles = new FakeTemporaryFileService(
            new ScanResult([], [], TimeSpan.Zero));
        var service = new CustomCleanService(temporaryFiles);

        await service.PreviewAsync([
            CustomCleanCategory.TemporaryFiles,
            CustomCleanCategory.TemporaryFiles
        ]);

        Assert.Equal(1, temporaryFiles.ScanCallCount);
        Assert.Equal(0, temporaryFiles.CleanCallCount);
    }

    [Fact]
    public async Task PreviewAsync_CancelledToken_StopsBeforeScanning()
    {
        var temporaryFiles = new FakeTemporaryFileService(
            new ScanResult([], [], TimeSpan.Zero));
        var service = new CustomCleanService(temporaryFiles);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.PreviewAsync(
                [CustomCleanCategory.TemporaryFiles],
                cancellationToken: cancellationTokenSource.Token));

        Assert.Equal(0, temporaryFiles.ScanCallCount);
        Assert.Equal(0, temporaryFiles.CleanCallCount);
    }

    [Fact]
    public async Task CleanAsync_SelectedCategory_CleansDistinctMatchingItemsAndMapsOutcomes()
    {
        var pathA = Path.Combine(Path.GetTempPath(), "custom-clean-a.tmp");
        var pathB = Path.Combine(Path.GetTempPath(), "custom-clean-b.tmp");
        var pathC = Path.Combine(Path.GetTempPath(), "custom-clean-c.tmp");
        var pathOutsideCategory = Path.Combine(
            Path.GetTempPath(),
            "custom-clean-unsupported.tmp");
        var now = DateTime.UtcNow;
        var temporaryFiles = new FakeTemporaryFileService(
            new ScanResult([], [], TimeSpan.Zero),
            new CleanupResult(
                1,
                1024,
                [
                    $"Blocked unsafe path: {pathB}",
                    $"Could not delete '{pathC}': the file is locked."
                ],
                TimeSpan.FromMilliseconds(25)));
        var service = new CustomCleanService(temporaryFiles);

        var result = await service.CleanAsync(
            [CustomCleanCategory.TemporaryFiles],
            [
                new CustomCleanPreviewItem(
                    CustomCleanCategory.TemporaryFiles,
                    pathA,
                    1024,
                    now),
                new CustomCleanPreviewItem(
                    CustomCleanCategory.TemporaryFiles,
                    pathA,
                    1024,
                    now),
                new CustomCleanPreviewItem(
                    CustomCleanCategory.TemporaryFiles,
                    pathB,
                    2048,
                    now),
                new CustomCleanPreviewItem(
                    CustomCleanCategory.TemporaryFiles,
                    pathC,
                    4096,
                    now),
                new CustomCleanPreviewItem(
                    (CustomCleanCategory)999,
                    pathOutsideCategory,
                    8192,
                    now)
            ]);

        Assert.Equal(1, temporaryFiles.CleanCallCount);
        Assert.Equal(3, temporaryFiles.LastCleanCandidates.Count);
        Assert.DoesNotContain(
            pathOutsideCategory,
            temporaryFiles.LastCleanCandidates.Select(candidate => candidate.FullPath));
        Assert.Equal(3, result.RequestedCount);
        Assert.Equal(1, result.DeletedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(1024, result.ReclaimedBytes);
        Assert.Equal(2, result.Errors.Count);
        Assert.False(result.CompletedWithoutIssues);
    }

    [Fact]
    public async Task CleanAsync_MissingOrAlreadyRemovedItem_CountsAsSkipped()
    {
        var now = DateTime.UtcNow;
        var temporaryFiles = new FakeTemporaryFileService(
            new ScanResult([], [], TimeSpan.Zero),
            new CleanupResult(
                1,
                512,
                [],
                TimeSpan.FromMilliseconds(5)));
        var service = new CustomCleanService(temporaryFiles);

        var result = await service.CleanAsync(
            [CustomCleanCategory.TemporaryFiles],
            [
                new CustomCleanPreviewItem(
                    CustomCleanCategory.TemporaryFiles,
                    Path.Combine(Path.GetTempPath(), "custom-clean-one.tmp"),
                    512,
                    now),
                new CustomCleanPreviewItem(
                    CustomCleanCategory.TemporaryFiles,
                    Path.Combine(Path.GetTempPath(), "custom-clean-missing.tmp"),
                    256,
                    now)
            ]);

        Assert.Equal(2, result.RequestedCount);
        Assert.Equal(1, result.DeletedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.False(result.CompletedWithoutIssues);
    }

    [Fact]
    public async Task CleanAsync_NoSelectedCategories_ReturnsEmptyWithoutCleaning()
    {
        var temporaryFiles = new FakeTemporaryFileService(
            new ScanResult([], [], TimeSpan.Zero));
        var service = new CustomCleanService(temporaryFiles);

        var result = await service.CleanAsync(
            [],
            [
                new CustomCleanPreviewItem(
                    CustomCleanCategory.TemporaryFiles,
                    Path.Combine(Path.GetTempPath(), "custom-clean.tmp"),
                    100,
                    DateTime.UtcNow)
            ]);

        Assert.Equal(0, result.RequestedCount);
        Assert.Equal(0, result.DeletedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(0, temporaryFiles.CleanCallCount);
    }

    [Fact]
    public async Task CleanAsync_UnsupportedCategory_DoesNotCallCleaner()
    {
        var temporaryFiles = new FakeTemporaryFileService(
            new ScanResult([], [], TimeSpan.Zero));
        var service = new CustomCleanService(temporaryFiles);

        var result = await service.CleanAsync(
            [(CustomCleanCategory)999],
            [
                new CustomCleanPreviewItem(
                    (CustomCleanCategory)999,
                    Path.Combine(Path.GetTempPath(), "custom-clean-unsupported.tmp"),
                    100,
                    DateTime.UtcNow)
            ]);

        Assert.Equal(0, result.RequestedCount);
        Assert.Equal(0, result.DeletedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Single(result.Errors);
        Assert.Equal(0, temporaryFiles.CleanCallCount);
    }

    [Fact]
    public async Task CleanAsync_CancelledToken_StopsBeforeCleaning()
    {
        var temporaryFiles = new FakeTemporaryFileService(
            new ScanResult([], [], TimeSpan.Zero));
        var service = new CustomCleanService(temporaryFiles);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.CleanAsync(
                [CustomCleanCategory.TemporaryFiles],
                [
                    new CustomCleanPreviewItem(
                        CustomCleanCategory.TemporaryFiles,
                        Path.Combine(Path.GetTempPath(), "custom-clean.tmp"),
                        100,
                        DateTime.UtcNow)
                ],
                cancellationToken: cancellationTokenSource.Token));

        Assert.Equal(0, temporaryFiles.CleanCallCount);
    }

    private sealed class FakeTemporaryFileService : ITemporaryFileService
    {
        private readonly ScanResult _scanResult;
        private readonly CleanupResult _cleanupResult;

        public FakeTemporaryFileService(
            ScanResult scanResult,
            CleanupResult? cleanupResult = null)
        {
            _scanResult = scanResult;
            _cleanupResult = cleanupResult ?? new CleanupResult(
                0,
                0,
                [],
                TimeSpan.Zero);
        }

        public int ScanCallCount { get; private set; }
        public int CleanCallCount { get; private set; }
        public IReadOnlyList<CleanupCandidate> LastCleanCandidates { get; private set; } = [];

        public Task<ScanResult> ScanAsync(
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScanCallCount++;
            progress?.Report(100);
            return Task.FromResult(_scanResult);
        }

        public Task<CleanupResult> CleanAsync(
            IReadOnlyCollection<CleanupCandidate> candidates,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CleanCallCount++;
            LastCleanCandidates = candidates.ToArray();
            progress?.Report(100);
            return Task.FromResult(_cleanupResult);
        }
    }
}
