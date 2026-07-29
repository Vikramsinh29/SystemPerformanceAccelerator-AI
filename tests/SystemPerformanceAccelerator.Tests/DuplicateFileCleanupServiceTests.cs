using System.Security.Cryptography;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Services;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class DuplicateFileCleanupServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"spa-duplicate-cleanup-tests-{Guid.NewGuid():N}");

    public DuplicateFileCleanupServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task CleanAsync_RecyclesSelectedCopyAndRetainsConfirmedKeeper()
    {
        var first = Path.Combine(_root, "first.bin");
        var second = Path.Combine(_root, "second.bin");
        var content = new byte[] { 1, 2, 3, 4, 5, 6 };
        await File.WriteAllBytesAsync(first, content);
        await File.WriteAllBytesAsync(second, content);
        var group = CreateGroup(first, second);
        var service = CreateService();

        var result = await service.CleanAsync(_root, [group], [group.Files[1]]);

        Assert.True(File.Exists(first));
        Assert.False(File.Exists(second));
        Assert.Equal(1, result.RecycledCount);
        Assert.Equal((long)content.Length, result.ReclaimedBytes);
        Assert.Equal(0, result.SkippedCount);
        Assert.Empty(result.Errors);
        Assert.False(result.WasCancelled);
    }

    [Fact]
    public async Task CleanAsync_RejectsSelectingEveryCopyInGroup()
    {
        var first = Path.Combine(_root, "first.bin");
        var second = Path.Combine(_root, "second.bin");
        await File.WriteAllBytesAsync(first, [1, 3, 5, 7]);
        await File.WriteAllBytesAsync(second, [1, 3, 5, 7]);
        var group = CreateGroup(first, second);
        var service = CreateService();

        var result = await service.CleanAsync(_root, [group], group.Files);

        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.Equal(0, result.RecycledCount);
        Assert.Equal(2, result.SkippedCount);
        var error = Assert.Single(result.Errors);
        Assert.Contains("at least one confirmed copy must remain", error.ToLowerInvariant());
    }

    [Fact]
    public async Task CleanAsync_SkipsSelectedFileChangedAfterScan()
    {
        var keeper = Path.Combine(_root, "keeper.bin");
        var changed = Path.Combine(_root, "changed.bin");
        await File.WriteAllBytesAsync(keeper, [2, 4, 6, 8]);
        await File.WriteAllBytesAsync(changed, [2, 4, 6, 8]);
        var group = CreateGroup(keeper, changed);

        await File.WriteAllBytesAsync(changed, [8, 6, 4, 2]);
        File.SetLastWriteTimeUtc(changed, group.Files[1].LastWriteTimeUtc.AddSeconds(2));
        var service = CreateService();

        var result = await service.CleanAsync(_root, [group], [group.Files[1]]);

        Assert.True(File.Exists(keeper));
        Assert.True(File.Exists(changed));
        Assert.Equal(0, result.RecycledCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Contains(result.Errors, error =>
            error.Contains("changed after the duplicate scan", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CleanAsync_SkipsGroupWhenNoVerifiedKeeperRemains()
    {
        var missingKeeper = Path.Combine(_root, "missing-keeper.bin");
        var selected = Path.Combine(_root, "selected.bin");
        await File.WriteAllBytesAsync(missingKeeper, [9, 7, 5, 3]);
        await File.WriteAllBytesAsync(selected, [9, 7, 5, 3]);
        var group = CreateGroup(missingKeeper, selected);
        File.Delete(missingKeeper);
        var service = CreateService();

        var result = await service.CleanAsync(_root, [group], [group.Files[1]]);

        Assert.True(File.Exists(selected));
        Assert.Equal(0, result.RecycledCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Contains(result.Errors, error =>
            error.Contains("no unchanged, accessible copy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CleanAsync_BlocksSelectedFileOutsideApprovedRoot()
    {
        var keeper = Path.Combine(_root, "keeper.bin");
        var outsideRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"spa-duplicate-outside-{Guid.NewGuid():N}")).FullName;
        var outside = Path.Combine(outsideRoot, "outside.bin");
        await File.WriteAllBytesAsync(keeper, [10, 20, 30, 40]);
        await File.WriteAllBytesAsync(outside, [10, 20, 30, 40]);
        var group = CreateGroup(keeper, outside);
        var service = CreateService();

        try
        {
            var result = await service.CleanAsync(_root, [group], [group.Files[1]]);

            Assert.True(File.Exists(keeper));
            Assert.True(File.Exists(outside));
            Assert.Equal(0, result.RecycledCount);
            Assert.Equal(1, result.SkippedCount);
            Assert.Contains(result.Errors, error =>
                error.Contains("outside the scanned location", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(outsideRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CleanAsync_SkipsLockedSelectedFile()
    {
        var keeper = Path.Combine(_root, "keeper.bin");
        var locked = Path.Combine(_root, "locked.bin");
        await File.WriteAllBytesAsync(keeper, [11, 22, 33, 44]);
        await File.WriteAllBytesAsync(locked, [11, 22, 33, 44]);
        var group = CreateGroup(keeper, locked);
        await using var lockStream = new FileStream(
            locked,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);
        var service = CreateService();

        var result = await service.CleanAsync(_root, [group], [group.Files[1]]);

        Assert.True(File.Exists(keeper));
        Assert.True(File.Exists(locked));
        Assert.Equal(0, result.RecycledCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.NotEmpty(result.Errors);
    }

    private static DuplicateFileCleanupService CreateService() =>
        new(new LargeFileCleanupService(File.Delete));

    private static DuplicateFileGroup CreateGroup(params string[] paths)
    {
        var candidates = paths
            .Select(path =>
            {
                var info = new FileInfo(path);
                using var stream = File.OpenRead(path);
                var hash = Convert.ToHexString(SHA256.HashData(stream));
                return new DuplicateFileCandidate(
                    info.FullName,
                    info.Length,
                    info.LastWriteTimeUtc,
                    hash);
            })
            .ToArray();

        return new DuplicateFileGroup(
            candidates[0].Sha256Hash,
            candidates[0].SizeBytes,
            candidates);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
