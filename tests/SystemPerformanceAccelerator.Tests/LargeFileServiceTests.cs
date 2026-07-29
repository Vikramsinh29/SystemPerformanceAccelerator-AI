using SystemPerformanceAccelerator.Infrastructure.Services;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class LargeFileServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"spa-large-file-tests-{Guid.NewGuid():N}");

    public LargeFileServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task ScanAsync_ReturnsOnlyMatchingFilesRecursivelyInDescendingSizeOrder()
    {
        var nested = Directory.CreateDirectory(Path.Combine(_root, "nested")).FullName;
        var smallFile = Path.Combine(_root, "small.bin");
        var mediumFile = Path.Combine(nested, "medium.bin");
        var largeFile = Path.Combine(_root, "large.bin");

        await File.WriteAllBytesAsync(smallFile, new byte[128]);
        await File.WriteAllBytesAsync(mediumFile, new byte[2_048]);
        await File.WriteAllBytesAsync(largeFile, new byte[4_096]);

        var service = new LargeFileService();
        var result = await service.ScanAsync(_root, minimumSizeBytes: 1_024);

        Assert.Equal(2, result.Candidates.Count);
        Assert.Equal(largeFile, result.Candidates[0].FullPath);
        Assert.Equal(mediumFile, result.Candidates[1].FullPath);
        Assert.DoesNotContain(result.Candidates, candidate => candidate.FullPath == smallFile);
        Assert.Equal(3, result.FilesScanned);
        Assert.True(result.DirectoriesScanned >= 2);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ScanAsync_ReturnsUsefulErrorForMissingLocation()
    {
        var missing = Path.Combine(_root, "does-not-exist");
        var service = new LargeFileService();

        var result = await service.ScanAsync(missing, minimumSizeBytes: 1);

        Assert.Empty(result.Candidates);
        var error = Assert.Single(result.Errors);
        Assert.Contains("does not exist", error.ToLowerInvariant());
    }

    [Fact]
    public async Task ScanAsync_HonorsCancellation()
    {
        var service = new LargeFileService();
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ScanAsync(
                _root,
                minimumSizeBytes: 1,
                cancellationToken: cancellationTokenSource.Token));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
