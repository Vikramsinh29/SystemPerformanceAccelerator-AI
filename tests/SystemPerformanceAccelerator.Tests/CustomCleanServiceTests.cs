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

    private sealed class FakeTemporaryFileService(ScanResult scanResult)
        : ITemporaryFileService
    {
        public int ScanCallCount { get; private set; }
        public int CleanCallCount { get; private set; }

        public Task<ScanResult> ScanAsync(
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScanCallCount++;
            progress?.Report(100);
            return Task.FromResult(scanResult);
        }

        public Task<CleanupResult> CleanAsync(
            IReadOnlyCollection<CleanupCandidate> candidates,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CleanCallCount++;
            return Task.FromResult(new CleanupResult(
                0,
                0,
                [],
                TimeSpan.Zero));
        }
    }
}
