using System.Diagnostics;
using System.Security;
using System.Security.Cryptography;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Services;

public sealed class DuplicateFileService : IDuplicateFileService
{
    private const int HashBufferSize = 128 * 1024;

    public Task<DuplicateFileScanResult> ScanAsync(
        string rootPath,
        IProgress<DuplicateFileScanProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => ScanCoreAsync(rootPath, progress, cancellationToken),
            cancellationToken);

    private static async Task<DuplicateFileScanResult> ScanCoreAsync(
        string rootPath,
        IProgress<DuplicateFileScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var filesBySize = new Dictionary<long, List<FileSnapshot>>();
        var duplicateGroups = new List<DuplicateFileGroup>();
        var errors = new List<string>();
        var filesScanned = 0;
        var directoriesScanned = 0;
        var filesHashed = 0;

        if (string.IsNullOrWhiteSpace(rootPath))
        {
            errors.Add("Choose a folder or drive before scanning.");
            return CreateResult();
        }

        string approvedRoot;
        try
        {
            approvedRoot = Path.GetFullPath(rootPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            errors.Add($"The selected location is invalid: {ex.Message}");
            return CreateResult();
        }

        if (!Directory.Exists(approvedRoot))
        {
            errors.Add($"The selected location does not exist: {approvedRoot}");
            return CreateResult();
        }

        try
        {
            if ((File.GetAttributes(approvedRoot) & FileAttributes.ReparsePoint) != 0)
            {
                errors.Add("The selected location is a reparse point and was not scanned.");
                return CreateResult();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            errors.Add($"Could not inspect the selected location: {ex.Message}");
            return CreateResult();
        }

        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(approvedRoot);

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentDirectory = pendingDirectories.Pop();
            directoriesScanned++;

            DiscoverFiles(currentDirectory);
            QueueChildDirectories(currentDirectory);

            progress?.Report(new DuplicateFileScanProgress(
                DuplicateFileScanPhase.DiscoveringFiles,
                filesScanned,
                directoriesScanned,
                0,
                0,
                currentDirectory));
        }

        var matchingSizeGroups = filesBySize
            .Where(pair => pair.Value.Count > 1)
            .OrderByDescending(pair => pair.Key)
            .ToArray();
        var hashCandidateCount = matchingSizeGroups.Sum(pair => pair.Value.Count);
        var hashCandidatesProcessed = 0;

        foreach (var sizeGroup in matchingSizeGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filesByHash = new Dictionary<string, List<FileSnapshot>>(StringComparer.Ordinal);

            foreach (var file in sizeGroup.Value)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var hash = await CalculateStableHashAsync(file, cancellationToken);
                    if (!filesByHash.TryGetValue(hash, out var matchingFiles))
                    {
                        matchingFiles = [];
                        filesByHash.Add(hash, matchingFiles);
                    }

                    matchingFiles.Add(file);
                    filesHashed++;
                }
                catch (FileChangedDuringScanException ex)
                {
                    errors.Add($"Skipped changed file '{file.FullPath}': {ex.Message}");
                }
                catch (Exception ex) when (ex is
                    IOException or
                    UnauthorizedAccessException or
                    SecurityException or
                    CryptographicException or
                    NotSupportedException)
                {
                    errors.Add($"Could not hash file '{file.FullPath}': {ex.Message}");
                }
                finally
                {
                    hashCandidatesProcessed++;
                    progress?.Report(new DuplicateFileScanProgress(
                        DuplicateFileScanPhase.HashingCandidates,
                        filesScanned,
                        directoriesScanned,
                        hashCandidatesProcessed,
                        hashCandidateCount,
                        file.FullPath));
                }
            }

            foreach (var hashGroup in filesByHash.Where(pair => pair.Value.Count > 1))
            {
                var candidates = hashGroup.Value
                    .OrderBy(file => file.FullPath, StringComparer.OrdinalIgnoreCase)
                    .Select(file => new DuplicateFileCandidate(
                        file.FullPath,
                        file.SizeBytes,
                        file.LastWriteTimeUtc,
                        hashGroup.Key))
                    .ToArray();

                duplicateGroups.Add(new DuplicateFileGroup(
                    hashGroup.Key,
                    sizeGroup.Key,
                    candidates));
            }
        }

        return CreateResult();

        void DiscoverFiles(string directory)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    filesScanned++;

                    try
                    {
                        var info = new FileInfo(file);
                        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }

                        if (!filesBySize.TryGetValue(info.Length, out var sameSizeFiles))
                        {
                            sameSizeFiles = [];
                            filesBySize.Add(info.Length, sameSizeFiles);
                        }

                        sameSizeFiles.Add(new FileSnapshot(
                            info.FullName,
                            info.Length,
                            info.LastWriteTimeUtc));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
                    {
                        errors.Add($"Skipped file '{file}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                errors.Add($"Could not scan files in '{directory}': {ex.Message}");
            }
        }

        void QueueChildDirectories(string directory)
        {
            try
            {
                foreach (var childDirectory in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var attributes = File.GetAttributes(childDirectory);
                        if ((attributes & FileAttributes.ReparsePoint) == 0)
                        {
                            pendingDirectories.Push(childDirectory);
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
                    {
                        errors.Add($"Skipped folder '{childDirectory}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                errors.Add($"Could not inspect folders in '{directory}': {ex.Message}");
            }
        }

        DuplicateFileScanResult CreateResult() => new(
            duplicateGroups
                .OrderByDescending(group => group.ReclaimableBytes)
                .ThenByDescending(group => group.SizeBytes)
                .ThenBy(group => group.Files[0].FullPath, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            errors.ToArray(),
            filesScanned,
            directoriesScanned,
            filesHashed,
            stopwatch.Elapsed);
    }

    private static async Task<string> CalculateStableHashAsync(
        FileSnapshot file,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            file.FullPath,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                BufferSize = HashBufferSize,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });

        if (stream.Length != file.SizeBytes)
        {
            throw new FileChangedDuringScanException("Its size changed before hashing completed.");
        }

        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);

        var refreshedInfo = new FileInfo(file.FullPath);
        refreshedInfo.Refresh();
        if (refreshedInfo.Length != file.SizeBytes ||
            refreshedInfo.LastWriteTimeUtc != file.LastWriteTimeUtc)
        {
            throw new FileChangedDuringScanException("Its size or modified time changed during the scan.");
        }

        return Convert.ToHexString(hashBytes);
    }

    private sealed record FileSnapshot(
        string FullPath,
        long SizeBytes,
        DateTime LastWriteTimeUtc);

    private sealed class FileChangedDuringScanException(string message) : IOException(message);
}
