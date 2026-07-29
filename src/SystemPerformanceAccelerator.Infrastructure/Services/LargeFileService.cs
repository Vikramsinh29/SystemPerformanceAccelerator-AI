using System.Diagnostics;
using System.Security;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Services;

public sealed class LargeFileService : ILargeFileService
{
    public Task<LargeFileScanResult> ScanAsync(
        string rootPath,
        long minimumSizeBytes,
        IProgress<LargeFileScanProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => Scan(rootPath, minimumSizeBytes, progress, cancellationToken),
            cancellationToken);

    private static LargeFileScanResult Scan(
        string rootPath,
        long minimumSizeBytes,
        IProgress<LargeFileScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var candidates = new List<LargeFileCandidate>();
        var errors = new List<string>();
        var filesScanned = 0;
        var directoriesScanned = 0;

        if (minimumSizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumSizeBytes),
                "Minimum file size cannot be negative.");
        }

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

        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(approvedRoot);

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentDirectory = pendingDirectories.Pop();
            directoriesScanned++;

            ScanFiles(currentDirectory);
            QueueChildDirectories(currentDirectory);

            progress?.Report(new LargeFileScanProgress(
                filesScanned,
                directoriesScanned,
                currentDirectory));
        }

        return CreateResult();

        void ScanFiles(string directory)
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

                        if (info.Length >= minimumSizeBytes)
                        {
                            candidates.Add(new LargeFileCandidate(
                                info.FullName,
                                info.Length,
                                info.LastWriteTimeUtc));
                        }
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

        LargeFileScanResult CreateResult() => new(
            candidates
                .OrderByDescending(candidate => candidate.SizeBytes)
                .ThenBy(candidate => candidate.FullPath, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            errors.ToArray(),
            filesScanned,
            directoriesScanned,
            stopwatch.Elapsed);
    }
}
