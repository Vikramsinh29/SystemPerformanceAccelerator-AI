using System.Diagnostics;
using System.Security;
using Microsoft.VisualBasic.FileIO;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Services;

public sealed class LargeFileCleanupService : ILargeFileCleanupService
{
    private static readonly string[] ProtectedDirectoryRoots = BuildProtectedDirectoryRoots();
    private static readonly string[] ProtectedDirectoryNames =
    [
        "$Recycle.Bin",
        "System Volume Information",
        "Recovery",
        "Boot"
    ];

    private static readonly HashSet<string> ProtectedSystemDriveFiles = new(
        [
            "bootmgr",
            "bootnxt",
            "bootstat.dat",
            "hiberfil.sys",
            "pagefile.sys",
            "swapfile.sys"
        ],
        StringComparer.OrdinalIgnoreCase);

    private readonly Action<string> _moveToRecycleBin;

    public LargeFileCleanupService()
        : this(MoveToRecycleBin)
    {
    }

    public LargeFileCleanupService(Action<string> moveToRecycleBin)
    {
        _moveToRecycleBin = moveToRecycleBin ?? throw new ArgumentNullException(nameof(moveToRecycleBin));
    }

    public Task<LargeFileCleanupResult> CleanAsync(
        string approvedRootPath,
        IReadOnlyCollection<LargeFileCandidate> candidates,
        IProgress<LargeFileCleanupProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => Clean(approvedRootPath, candidates, progress, cancellationToken),
            cancellationToken);

    private LargeFileCleanupResult Clean(
        string approvedRootPath,
        IReadOnlyCollection<LargeFileCandidate> candidates,
        IProgress<LargeFileCleanupProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var stopwatch = Stopwatch.StartNew();
        var recycledPaths = new List<string>();
        var errors = new List<string>();
        var reclaimedBytes = 0L;
        var processedCount = 0;

        if (!TryNormalizeDirectory(approvedRootPath, out var approvedRoot, out var rootError))
        {
            errors.Add(rootError);
            return CreateResult();
        }

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var fullPath = Path.GetFullPath(candidate.FullPath);

                if (!IsPathInsideDirectory(fullPath, approvedRoot))
                {
                    errors.Add($"Skipped '{candidate.Name}': the file is outside the scanned location.");
                    continue;
                }

                if (TryGetProtectionReason(fullPath, out var protectionReason))
                {
                    errors.Add($"Skipped '{candidate.Name}': {protectionReason}");
                    continue;
                }

                if (!File.Exists(fullPath))
                {
                    errors.Add($"Skipped '{candidate.Name}': the file no longer exists.");
                    continue;
                }

                var fileInfo = new FileInfo(fullPath);
                if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    errors.Add($"Skipped '{candidate.Name}': reparse-point files are protected.");
                    continue;
                }

                if ((fileInfo.Attributes & FileAttributes.System) != 0)
                {
                    errors.Add($"Skipped '{candidate.Name}': system files are protected.");
                    continue;
                }

                if ((fileInfo.Attributes & FileAttributes.ReadOnly) != 0)
                {
                    errors.Add($"Skipped '{candidate.Name}': the file is read-only.");
                    continue;
                }

                var currentSize = fileInfo.Length;
                _moveToRecycleBin(fullPath);

                if (File.Exists(fullPath))
                {
                    errors.Add($"Skipped '{candidate.Name}': Windows did not move the file to the Recycle Bin.");
                    continue;
                }

                recycledPaths.Add(fullPath);
                reclaimedBytes += currentSize;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException)
            {
                errors.Add($"Skipped '{candidate.Name}': {ex.Message}");
            }
            finally
            {
                processedCount++;
                progress?.Report(new LargeFileCleanupProgress(
                    processedCount,
                    candidates.Count,
                    candidate.FullPath));
            }
        }

        return CreateResult();

        LargeFileCleanupResult CreateResult() => new(
            recycledPaths.ToArray(),
            errors.ToArray(),
            reclaimedBytes,
            stopwatch.Elapsed);
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
            error = "The scanned location is missing. Run the scan again before cleanup.";
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

        return true;
    }

    private static bool TryGetProtectionReason(string fullPath, out string reason)
    {
        foreach (var protectedRoot in ProtectedDirectoryRoots)
        {
            if (IsPathInsideDirectory(fullPath, protectedRoot))
            {
                reason = $"files inside '{protectedRoot}' are protected.";
                return true;
            }
        }

        var pathSegments = fullPath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        if (pathSegments.Any(segment =>
                ProtectedDirectoryNames.Contains(segment, StringComparer.OrdinalIgnoreCase)))
        {
            reason = "the file is inside a Windows-protected folder.";
            return true;
        }

        var windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var systemDriveRoot = Path.GetPathRoot(windowsPath);
        var containingDirectory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(systemDriveRoot) &&
            !string.IsNullOrWhiteSpace(containingDirectory) &&
            string.Equals(
                Path.TrimEndingDirectorySeparator(containingDirectory),
                Path.TrimEndingDirectorySeparator(systemDriveRoot),
                StringComparison.OrdinalIgnoreCase) &&
            ProtectedSystemDriveFiles.Contains(Path.GetFileName(fullPath)))
        {
            reason = "Windows system-drive files are protected.";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static bool IsPathInsideDirectory(string fullPath, string directoryPath)
    {
        var relativePath = Path.GetRelativePath(directoryPath, fullPath);
        return !Path.IsPathRooted(relativePath) &&
               !string.Equals(relativePath, "..", StringComparison.Ordinal) &&
               !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static string[] BuildProtectedDirectoryRoots()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            AppContext.BaseDirectory
        };

        return roots
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void MoveToRecycleBin(string fullPath) =>
        FileSystem.DeleteFile(
            fullPath,
            UIOption.OnlyErrorDialogs,
            RecycleOption.SendToRecycleBin,
            UICancelOption.ThrowException);
}
