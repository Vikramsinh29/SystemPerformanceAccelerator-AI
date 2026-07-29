using System.Diagnostics;
using System.Security;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Services;

public sealed class TemporaryFileService : ITemporaryFileService
{
    private const int DeleteRetryDelayMilliseconds = 75;
    private readonly string _approvedRoot;

    public TemporaryFileService(string? approvedRoot = null)
    {
        _approvedRoot = Path.GetFullPath(approvedRoot ?? Path.GetTempPath())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public Task<ScanResult> ScanAsync(
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(progress, cancellationToken), cancellationToken);

    public Task<CleanupResult> CleanAsync(
        IReadOnlyCollection<CleanupCandidate> candidates,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Clean(candidates, progress, cancellationToken), cancellationToken);

    private ScanResult Scan(IProgress<int>? progress, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var candidates = new List<CleanupCandidate>();
        var errors = new List<string>();

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(_approvedRoot, "*", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add($"Unable to scan temporary folder: {ex.Message}");
            return new ScanResult(candidates, errors, stopwatch.Elapsed);
        }

        var processed = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(file);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                candidates.Add(new CleanupCandidate(info.FullName, info.Length, info.LastWriteTimeUtc));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"Skipped '{file}': {ex.Message}");
            }

            processed++;
            progress?.Report(Math.Min(99, processed));
        }

        progress?.Report(100);
        return new ScanResult(candidates.OrderByDescending(x => x.SizeBytes).ToArray(), errors, stopwatch.Elapsed);
    }

    private CleanupResult Clean(
        IReadOnlyCollection<CleanupCandidate> candidates,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();
        var deletedCount = 0;
        long reclaimedBytes = 0;
        var total = Math.Max(1, candidates.Count);
        var index = 0;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;

            try
            {
                var fullPath = Path.GetFullPath(candidate.FullPath);
                if (!IsWithinApprovedRoot(fullPath))
                {
                    errors.Add($"Blocked unsafe path: {candidate.FullPath}");
                    continue;
                }

                if (!File.Exists(fullPath))
                {
                    continue;
                }

                var info = new FileInfo(fullPath);
                var attributes = info.Attributes;

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    errors.Add($"Skipped reparse point: {candidate.FullPath}");
                    continue;
                }

                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    errors.Add($"Could not delete '{candidate.FullPath}': the file is read-only.");
                    continue;
                }

                var actualLength = info.Length;
                DeleteWithSingleRetry(fullPath, cancellationToken);

                if (File.Exists(fullPath))
                {
                    errors.Add($"Could not delete '{candidate.FullPath}': the file remained available after deletion was requested.");
                    continue;
                }

                deletedCount++;
                reclaimedBytes += actualLength;
            }
            catch (IOException ex)
            {
                errors.Add($"Could not delete '{candidate.FullPath}': {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                errors.Add($"Could not delete '{candidate.FullPath}': access was denied. {ex.Message}");
            }
            catch (SecurityException ex)
            {
                errors.Add($"Could not delete '{candidate.FullPath}': Windows security blocked access. {ex.Message}");
            }
            finally
            {
                progress?.Report(index * 100 / total);
            }
        }

        return new CleanupResult(deletedCount, reclaimedBytes, errors, stopwatch.Elapsed);
    }

    private static void DeleteWithSingleRetry(string fullPath, CancellationToken cancellationToken)
    {
        try
        {
            File.Delete(fullPath);
        }
        catch (IOException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Thread.Sleep(DeleteRetryDelayMilliseconds);
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(fullPath);
        }
    }

    private bool IsWithinApprovedRoot(string fullPath)
    {
        var relative = Path.GetRelativePath(_approvedRoot, fullPath);
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }
}
