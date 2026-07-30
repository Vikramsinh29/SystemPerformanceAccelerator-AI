using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Services;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class TemporaryFileServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"spa-tests-{Guid.NewGuid():N}");

    public TemporaryFileServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task ScanAsync_ReturnsFilesFromApprovedRoot()
    {
        var file = Path.Combine(_root, "sample.tmp");
        await File.WriteAllTextAsync(file, "temporary content");
        var service = new TemporaryFileService(_root);

        var result = await service.ScanAsync();

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(file, candidate.FullPath);
        Assert.True(candidate.SizeBytes > 0);
    }

    [Fact]
    public async Task CleanAsync_DeletesApprovedFile()
    {
        var file = Path.Combine(_root, "delete.tmp");
        await File.WriteAllTextAsync(file, "temporary content");
        var info = new FileInfo(file);
        var service = new TemporaryFileService(_root);

        var result = await service.CleanAsync([
            new CleanupCandidate(file, info.Length, info.LastWriteTimeUtc)
        ]);

        Assert.False(File.Exists(file));
        Assert.Equal(1, result.DeletedCount);
        Assert.True(result.ReclaimedBytes > 0);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task CleanAsync_SkipsFileChangedAfterScan()
    {
        var file = Path.Combine(_root, "changed-after-scan.tmp");
        await File.WriteAllTextAsync(file, "original temporary content");
        var info = new FileInfo(file);
        var candidate = new CleanupCandidate(file, info.Length, info.LastWriteTimeUtc);
        var service = new TemporaryFileService(_root);

        await File.AppendAllTextAsync(file, " changed");

        var result = await service.CleanAsync([candidate]);

        Assert.True(File.Exists(file));
        Assert.Equal(0, result.DeletedCount);
        Assert.Equal(0, result.ReclaimedBytes);
        var error = Assert.Single(result.Errors);
        Assert.Contains("changed after the scan", error.ToLowerInvariant());
    }

    [Fact]
    public async Task CleanAsync_BlocksFileOutsideApprovedRoot()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(outside, "do not delete");
        try
        {
            var info = new FileInfo(outside);
            var service = new TemporaryFileService(_root);

            var result = await service.CleanAsync([
                new CleanupCandidate(outside, info.Length, info.LastWriteTimeUtc)
            ]);

            Assert.True(File.Exists(outside));
            Assert.Equal(0, result.DeletedCount);
            Assert.Single(result.Errors);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task CleanAsync_ContinuesWhenOneFileIsLocked()
    {
        var lockedFile = Path.Combine(_root, "locked.tmp");
        var deletableFile = Path.Combine(_root, "deletable.tmp");
        await File.WriteAllTextAsync(lockedFile, "locked content");
        await File.WriteAllTextAsync(deletableFile, "deletable content");

        var lockedInfo = new FileInfo(lockedFile);
        var deletableInfo = new FileInfo(deletableFile);
        var service = new TemporaryFileService(_root);

        await using var lockStream = new FileStream(
            lockedFile,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        var result = await service.CleanAsync([
            new CleanupCandidate(lockedFile, lockedInfo.Length, lockedInfo.LastWriteTimeUtc),
            new CleanupCandidate(deletableFile, deletableInfo.Length, deletableInfo.LastWriteTimeUtc)
        ]);

        Assert.True(File.Exists(lockedFile));
        Assert.False(File.Exists(deletableFile));
        Assert.Equal(1, result.DeletedCount);
        var error = Assert.Single(result.Errors);
        Assert.Contains(lockedFile, error);
    }

    [Fact]
    public async Task CleanAsync_SkipsReadOnlyFileWithoutChangingItsAttributes()
    {
        var file = Path.Combine(_root, "read-only.tmp");
        await File.WriteAllTextAsync(file, "protected temporary content");
        File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);

        try
        {
            var info = new FileInfo(file);
            var service = new TemporaryFileService(_root);

            var result = await service.CleanAsync([
                new CleanupCandidate(file, info.Length, info.LastWriteTimeUtc)
            ]);

            Assert.True(File.Exists(file));
            Assert.Equal(0, result.DeletedCount);
            var error = Assert.Single(result.Errors);
            Assert.Contains("read-only", error.ToLowerInvariant());
            Assert.True((File.GetAttributes(file) & FileAttributes.ReadOnly) != 0);
        }
        finally
        {
            if (File.Exists(file))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }
    }
}
