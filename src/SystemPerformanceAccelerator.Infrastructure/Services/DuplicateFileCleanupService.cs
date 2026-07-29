using System.Diagnostics;
using System.Security;
using System.Security.Cryptography;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Services;

public sealed class DuplicateFileCleanupService : IDuplicateFileCleanupService
{
    private const int HashBufferSize = 128 * 1024;
    private readonly ILargeFileCleanupService _largeFileCleanupService;

    public DuplicateFileCleanupService(ILargeFileCleanupService largeFileCleanupService)
    {
        _largeFileCleanupService = largeFileCleanupService ??
            throw new ArgumentNullException(nameof(largeFileCleanupService));
    }

    public async Task<DuplicateFileCleanupResult> CleanAsync(
        string approvedRootPath,
        IReadOnlyCollection<DuplicateFileGroup> confirmedGroups,
        IReadOnlyCollection<DuplicateFileCandidate> selectedCandidates,
        IProgress<DuplicateFileCleanupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(confirmedGroups);
        ArgumentNullException.ThrowIfNull(selectedCandidates);

        var stopwatch = Stopwatch.StartNew();
        var recycledPaths = new List<string>();
        var errors = new List<string>();
        var reclaimedBytes = 0L;
        var skippedCount = 0;
        var processedCount = 0;
        var wasCancelled = false;

        var selectedPaths = selectedCandidates
            .Select(candidate => candidate.FullPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var totalCount = selectedPaths.Count;
        var matchedSelectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!TryNormalizeDirectory(approvedRootPath, out var approvedRoot, out var rootError))
        {
            if (totalCount > 0)
            {
                skippedCount = totalCount;
                errors.Add(rootError);
            }

            return CreateResult();
        }

        try
        {
            foreach (var group in confirmedGroups)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var canonicalSelected = group.Files
                    .Where(candidate => selectedPaths.Contains(candidate.FullPath))
                    .GroupBy(candidate => candidate.FullPath, StringComparer.OrdinalIgnoreCase)
                    .Select(candidateGroup => candidateGroup.First())
                    .ToArray();

                if (canonicalSelected.Length == 0)
                {
                    continue;
                }

                foreach (var candidate in canonicalSelected)
                {
                    matchedSelectedPaths.Add(candidate.FullPath);
                }

                if (group.Files.Count < 2 || canonicalSelected.Length >= group.Files.Count)
                {
                    errors.Add(
                        $"Skipped {canonicalSelected.Length:N0} selected file(s) in {FormatGroup(group)}: at least one confirmed copy must remain.");
                    SkipCandidates(canonicalSelected);
                    continue;
                }

                var keeperCandidates = group.Files
                    .Where(candidate => !selectedPaths.Contains(candidate.FullPath))
                    .ToArray();

                await using var keeperStream = await OpenVerifiedKeeperAsync(
                    approvedRoot,
                    keeperCandidates,
                    cancellationToken);

                if (keeperStream is null)
                {
                    errors.Add(
                        $"Skipped {canonicalSelected.Length:N0} selected file(s) in {FormatGroup(group)}: no unchanged, accessible copy could be retained.");
                    SkipCandidates(canonicalSelected);
                    continue;
                }

                foreach (var candidate in canonicalSelected)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var fullPath = Path.GetFullPath(candidate.FullPath);
                        if (!IsPathInsideDirectory(fullPath, approvedRoot))
                        {
                            skippedCount++;
                            errors.Add($"Skipped '{candidate.Name}': the file is outside the scanned location.");
                            continue;
                        }

                        await using var selectedStream = await OpenVerifiedFileAsync(
                            candidate,
                            approvedRoot,
                            allowDelete: true,
                            cancellationToken);

                        var cleanupResult = await _largeFileCleanupService.CleanAsync(
                            approvedRoot,
                            [new LargeFileCandidate(
                                candidate.FullPath,
                                candidate.SizeBytes,
                                candidate.LastWriteTimeUtc)],
                            cancellationToken: cancellationToken);

                        if (cleanupResult.RecycledCount == 1)
                        {
                            recycledPaths.Add(cleanupResult.RecycledPaths[0]);
                            reclaimedBytes = SaturatingAdd(
                                reclaimedBytes,
                                cleanupResult.ReclaimedBytes);
                        }
                        else
                        {
                            skippedCount++;
                            if (cleanupResult.Errors.Count > 0)
                            {
                                errors.AddRange(cleanupResult.Errors);
                            }
                            else
                            {
                                errors.Add($"Skipped '{candidate.Name}': Windows did not move the file to the Recycle Bin.");
                            }
                        }
                    }
                    catch (DuplicateFileChangedException ex)
                    {
                        skippedCount++;
                        errors.Add($"Skipped '{candidate.Name}': {ex.Message}");
                    }
                    catch (Exception ex) when (ex is
                        IOException or
                        UnauthorizedAccessException or
                        SecurityException or
                        CryptographicException or
                        ArgumentException or
                        NotSupportedException)
                    {
                        skippedCount++;
                        errors.Add($"Skipped '{candidate.Name}': {ex.Message}");
                    }
                    finally
                    {
                        ReportProcessed(candidate.FullPath);
                    }
                }
            }

            foreach (var unmatchedPath in selectedPaths.Except(
                         matchedSelectedPaths,
                         StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                skippedCount++;
                errors.Add($"Skipped '{Path.GetFileName(unmatchedPath)}': it is not part of the current confirmed duplicate results.");
                ReportProcessed(unmatchedPath);
            }
        }
        catch (OperationCanceledException)
        {
            wasCancelled = true;
        }

        return CreateResult();

        void SkipCandidates(IEnumerable<DuplicateFileCandidate> candidates)
        {
            foreach (var candidate in candidates)
            {
                skippedCount++;
                ReportProcessed(candidate.FullPath);
            }
        }

        void ReportProcessed(string currentFile)
        {
            processedCount++;
            progress?.Report(new DuplicateFileCleanupProgress(
                processedCount,
                totalCount,
                currentFile));
        }

        DuplicateFileCleanupResult CreateResult() => new(
            recycledPaths.ToArray(),
            skippedCount,
            errors.ToArray(),
            reclaimedBytes,
            stopwatch.Elapsed,
            wasCancelled);
    }

    private static async Task<FileStream?> OpenVerifiedKeeperAsync(
        string approvedRoot,
        IReadOnlyCollection<DuplicateFileCandidate> keeperCandidates,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in keeperCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var fullPath = Path.GetFullPath(candidate.FullPath);
                if (!IsPathInsideDirectory(fullPath, approvedRoot))
                {
                    continue;
                }

                return await OpenVerifiedFileAsync(
                    candidate,
                    approvedRoot,
                    allowDelete: false,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is
                IOException or
                UnauthorizedAccessException or
                SecurityException or
                CryptographicException or
                ArgumentException or
                NotSupportedException)
            {
                _ = ex;
                // Try another unselected member of the same confirmed group.
            }
        }

        return null;
    }

    private static async Task<FileStream> OpenVerifiedFileAsync(
        DuplicateFileCandidate candidate,
        string approvedRoot,
        bool allowDelete,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(candidate.FullPath);
        if (!IsPathInsideDirectory(fullPath, approvedRoot))
        {
            throw new DuplicateFileChangedException("the file is outside the scanned location.");
        }

        if (PathContainsReparsePoint(fullPath, approvedRoot))
        {
            throw new DuplicateFileChangedException("the file path contains a reparse point and is protected.");
        }

        var attributes = File.GetAttributes(fullPath);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new DuplicateFileChangedException("reparse-point files are protected.");
        }

        var stream = new FileStream(
            fullPath,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = allowDelete
                    ? FileShare.Delete
                    : FileShare.None,
                BufferSize = HashBufferSize,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });

        try
        {
            var beforeWriteTime = File.GetLastWriteTimeUtc(fullPath);
            if (stream.Length != candidate.SizeBytes ||
                beforeWriteTime != candidate.LastWriteTimeUtc)
            {
                throw new DuplicateFileChangedException("the file changed after the duplicate scan. Run the scan again.");
            }

            var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
            var hash = Convert.ToHexString(hashBytes);
            var afterWriteTime = File.GetLastWriteTimeUtc(fullPath);

            if (stream.Length != candidate.SizeBytes ||
                afterWriteTime != candidate.LastWriteTimeUtc ||
                !string.Equals(hash, candidate.Sha256Hash, StringComparison.Ordinal))
            {
                throw new DuplicateFileChangedException("the file content changed after the duplicate scan. Run the scan again.");
            }

            return stream;
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    private static bool TryNormalizeDirectory(
        string path,
        out string normalizedPath,
        out string error)
    {
        normalizedPath = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "The scanned location is missing. Run the duplicate scan again before cleanup.";
            return false;
        }

        try
        {
            normalizedPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"The scanned location is invalid: {ex.Message}";
            return false;
        }

        if (!Directory.Exists(normalizedPath))
        {
            error = $"The scanned location no longer exists: {normalizedPath}";
            return false;
        }

        try
        {
            if ((File.GetAttributes(normalizedPath) & FileAttributes.ReparsePoint) != 0)
            {
                error = "The scanned location is now a reparse point. Run the duplicate scan again.";
                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            error = $"The scanned location could not be verified: {ex.Message}";
            return false;
        }

        return true;
    }


    private static bool PathContainsReparsePoint(string fullPath, string approvedRoot)
    {
        var currentDirectory = Path.GetDirectoryName(fullPath);
        var normalizedRoot = Path.TrimEndingDirectorySeparator(approvedRoot);

        while (!string.IsNullOrWhiteSpace(currentDirectory))
        {
            if ((File.GetAttributes(currentDirectory) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }

            var normalizedCurrent = Path.TrimEndingDirectorySeparator(currentDirectory);
            if (string.Equals(normalizedCurrent, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var parent = Path.GetDirectoryName(currentDirectory);
            if (string.IsNullOrWhiteSpace(parent) ||
                string.Equals(parent, currentDirectory, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            currentDirectory = parent;
        }

        return true;
    }

    private static bool IsPathInsideDirectory(string fullPath, string directoryPath)
    {
        var relativePath = Path.GetRelativePath(directoryPath, fullPath);
        return !Path.IsPathRooted(relativePath) &&
               !string.Equals(relativePath, "..", StringComparison.Ordinal) &&
               !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static string FormatGroup(DuplicateFileGroup group)
    {
        var hashPrefix = group.Sha256Hash[..Math.Min(12, group.Sha256Hash.Length)];
        return $"duplicate group {hashPrefix}";
    }

    private static long SaturatingAdd(long left, long right) =>
        right > long.MaxValue - left ? long.MaxValue : left + right;

    private sealed class DuplicateFileChangedException(string message) : IOException(message);
}
