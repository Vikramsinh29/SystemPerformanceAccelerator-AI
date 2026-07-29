using SystemPerformanceAccelerator.Infrastructure.Services;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class DuplicateFileServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"spa-duplicate-tests-{Guid.NewGuid():N}");

    public DuplicateFileServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task ScanAsync_FindsContentConfirmedDuplicatesAndIgnoresMatchingNames()
    {
        var nested = Directory.CreateDirectory(Path.Combine(_root, "nested")).FullName;
        var duplicateContent = new byte[] { 1, 2, 3, 4, 5 };
        var firstDuplicate = Path.Combine(_root, "original.bin");
        var renamedDuplicate = Path.Combine(nested, "renamed-copy.dat");
        var firstSameName = Path.Combine(_root, "same-name.bin");
        var secondSameName = Path.Combine(nested, "same-name.bin");
        var uniqueSize = Path.Combine(_root, "unique-size.bin");

        await File.WriteAllBytesAsync(firstDuplicate, duplicateContent);
        await File.WriteAllBytesAsync(renamedDuplicate, duplicateContent);
        await File.WriteAllBytesAsync(firstSameName, new byte[] { 6, 7, 8, 9 });
        await File.WriteAllBytesAsync(secondSameName, new byte[] { 9, 8, 7, 6 });
        await File.WriteAllBytesAsync(uniqueSize, new byte[] { 10, 11, 12 });

        var service = new DuplicateFileService();
        var result = await service.ScanAsync(_root);

        var group = Assert.Single(result.Groups);
        Assert.Equal(2, group.Files.Count);
        Assert.Contains(group.Files, file => file.FullPath == firstDuplicate);
        Assert.Contains(group.Files, file => file.FullPath == renamedDuplicate);
        Assert.DoesNotContain(group.Files, file => file.FullPath == firstSameName);
        Assert.DoesNotContain(group.Files, file => file.FullPath == secondSameName);
        Assert.Equal((long)duplicateContent.Length, group.ReclaimableBytes);
        Assert.Equal(4, result.FilesHashed);
        Assert.Equal(5, result.FilesScanned);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ScanAsync_ReportsPotentialReclaimableBytesPerDuplicateGroup()
    {
        var content = new byte[2_048];
        var first = Path.Combine(_root, "first.bin");
        var second = Path.Combine(_root, "second.bin");
        var third = Path.Combine(_root, "third.bin");

        await File.WriteAllBytesAsync(first, content);
        await File.WriteAllBytesAsync(second, content);
        await File.WriteAllBytesAsync(third, content);

        var service = new DuplicateFileService();
        var result = await service.ScanAsync(_root);

        var group = Assert.Single(result.Groups);
        Assert.Equal(3, group.Files.Count);
        Assert.Equal(4_096L, group.ReclaimableBytes);
        Assert.Equal(4_096L, result.PotentialReclaimableBytes);
        Assert.Equal(3, result.DuplicateFileCount);
    }

    [Fact]
    public async Task ScanAsync_SkipsLockedFileWithoutChangingIt()
    {
        var first = Path.Combine(_root, "first.bin");
        var locked = Path.Combine(_root, "locked.bin");
        var content = new byte[] { 1, 3, 5, 7, 9, 11 };

        await File.WriteAllBytesAsync(first, content);
        await File.WriteAllBytesAsync(locked, content);

        await using var lockStream = new FileStream(
            locked,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        var service = new DuplicateFileService();
        var result = await service.ScanAsync(_root);

        Assert.Empty(result.Groups);
        Assert.Contains(result.Errors, error =>
            error.Contains("Could not hash file", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(locked));
        Assert.Equal((long)content.Length, new FileInfo(locked).Length);
    }

    [Fact]
    public async Task ScanAsync_ReturnsUsefulErrorForMissingLocation()
    {
        var missing = Path.Combine(_root, "does-not-exist");
        var service = new DuplicateFileService();

        var result = await service.ScanAsync(missing);

        Assert.Empty(result.Groups);
        var error = Assert.Single(result.Errors);
        Assert.Contains("does not exist", error.ToLowerInvariant());
    }

    [Fact]
    public async Task ScanAsync_HonorsCancellation()
    {
        var service = new DuplicateFileService();
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ScanAsync(
                _root,
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
