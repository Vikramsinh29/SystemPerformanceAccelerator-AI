using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Services;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class LargeFileCleanupServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"spa-large-cleanup-tests-{Guid.NewGuid():N}");

    public LargeFileCleanupServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task CleanAsync_RecyclesSafeFileAndReportsReclaimedBytes()
    {
        var filePath = Path.Combine(_root, "safe-large-file.bin");
        await File.WriteAllBytesAsync(filePath, new byte[4_096]);
        var candidate = CreateCandidate(filePath);
        var service = new LargeFileCleanupService(File.Delete);

        var result = await service.CleanAsync(_root, [candidate]);

        Assert.False(File.Exists(filePath));
        Assert.Equal(1, result.RecycledCount);
        Assert.Equal(4_096, result.ReclaimedBytes);
        Assert.Empty(result.Errors);
        Assert.Contains(result.RecycledPaths, path => string.Equals(path, filePath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CleanAsync_BlocksFilesOutsideApprovedScanRoot()
    {
        var outsideRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"spa-outside-{Guid.NewGuid():N}")).FullName;
        var outsideFile = Path.Combine(outsideRoot, "outside.bin");
        await File.WriteAllBytesAsync(outsideFile, new byte[256]);
        var recycleCalled = false;
        var service = new LargeFileCleanupService(_ => recycleCalled = true);

        try
        {
            var result = await service.CleanAsync(_root, [CreateCandidate(outsideFile)]);

            Assert.False(recycleCalled);
            Assert.True(File.Exists(outsideFile));
            Assert.Empty(result.RecycledPaths);
            var error = Assert.Single(result.Errors);
            Assert.Contains("outside the scanned location", error.ToLowerInvariant());
        }
        finally
        {
            Directory.Delete(outsideRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CleanAsync_BlocksWindowsProtectedLocationBeforeRecycleOperation()
    {
        var windowsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var protectedPath = Path.Combine(windowsFolder, "System32", "spa-protected-test.bin");
        var recycleCalled = false;
        var service = new LargeFileCleanupService(_ => recycleCalled = true);

        var result = await service.CleanAsync(windowsFolder, [
            new LargeFileCandidate(protectedPath, 1_024, DateTime.UtcNow)
        ]);

        Assert.False(recycleCalled);
        Assert.Empty(result.RecycledPaths);
        var error = Assert.Single(result.Errors);
        Assert.Contains("protected", error.ToLowerInvariant());
    }

    [Fact]
    public async Task CleanAsync_SkipsFileWhenRecycleOperationFails()
    {
        var filePath = Path.Combine(_root, "locked-large-file.bin");
        await File.WriteAllBytesAsync(filePath, new byte[512]);
        var service = new LargeFileCleanupService(_ => throw new IOException("The file is in use."));

        var result = await service.CleanAsync(_root, [CreateCandidate(filePath)]);

        Assert.True(File.Exists(filePath));
        Assert.Empty(result.RecycledPaths);
        Assert.Equal(0, result.ReclaimedBytes);
        var error = Assert.Single(result.Errors);
        Assert.Contains("in use", error.ToLowerInvariant());
    }

    private static LargeFileCandidate CreateCandidate(string path)
    {
        var info = new FileInfo(path);
        return new LargeFileCandidate(info.FullName, info.Length, info.LastWriteTimeUtc);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
