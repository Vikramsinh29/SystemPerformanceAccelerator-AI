using System.Diagnostics;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Services;

public sealed class CustomCleanService : ICustomCleanService
{
    private readonly ITemporaryFileService _temporaryFileService;

    public CustomCleanService(ITemporaryFileService temporaryFileService)
    {
        _temporaryFileService = temporaryFileService ??
            throw new ArgumentNullException(nameof(temporaryFileService));
    }

    public async Task<CustomCleanPreviewResult> PreviewAsync(
        IReadOnlyCollection<CustomCleanCategory> categories,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(categories);
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        var items = new List<CustomCleanPreviewItem>();
        var errors = new List<string>();
        var selectedCategories = categories.Distinct().ToArray();

        if (selectedCategories.Length == 0)
        {
            progress?.Report(100);
            return new CustomCleanPreviewResult(items, errors, stopwatch.Elapsed);
        }

        foreach (var category in selectedCategories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (category)
            {
                case CustomCleanCategory.TemporaryFiles:
                    await PreviewTemporaryFilesAsync(
                        items,
                        errors,
                        progress,
                        cancellationToken);
                    break;

                default:
                    errors.Add($"Unsupported Custom Clean category: {category}.");
                    break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(100);

        return new CustomCleanPreviewResult(
            items.OrderByDescending(item => item.SizeBytes).ToArray(),
            errors,
            stopwatch.Elapsed);
    }

    public async Task<CustomCleanExecutionResult> CleanAsync(
        IReadOnlyCollection<CustomCleanCategory> categories,
        IReadOnlyCollection<CustomCleanPreviewItem> items,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(items);
        cancellationToken.ThrowIfCancellationRequested();

        var distinctCategories = categories
            .Distinct()
            .ToArray();
        var unsupportedErrors = distinctCategories
            .Where(category => category != CustomCleanCategory.TemporaryFiles)
            .Select(category => $"Unsupported Custom Clean category: {category}.")
            .ToArray();
        var selectedCategories = distinctCategories
            .Where(category => category == CustomCleanCategory.TemporaryFiles)
            .ToHashSet();

        var candidates = items
            .Where(item => selectedCategories.Contains(item.Category))
            .GroupBy(
                item => item.FullPath,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(item => new CleanupCandidate(
                item.FullPath,
                item.SizeBytes,
                item.LastWriteTimeUtc))
            .ToArray();

        if (candidates.Length == 0)
        {
            progress?.Report(100);
            return new CustomCleanExecutionResult(
                0,
                0,
                0,
                unsupportedErrors.Length,
                0,
                unsupportedErrors,
                TimeSpan.Zero);
        }

        var result = await _temporaryFileService.CleanAsync(
            candidates,
            progress,
            cancellationToken);

        var explicitSkippedCount = result.Errors.Count(IsSafeSkip);
        var failedCount = result.Errors.Count - explicitSkippedCount;
        var missingOrAlreadyRemovedCount = Math.Max(
            0,
            candidates.Length - result.DeletedCount - result.Errors.Count);

        return new CustomCleanExecutionResult(
            candidates.Length,
            result.DeletedCount,
            explicitSkippedCount + missingOrAlreadyRemovedCount,
            failedCount,
            result.ReclaimedBytes,
            result.Errors,
            result.Elapsed);
    }

    private async Task PreviewTemporaryFilesAsync(
        ICollection<CustomCleanPreviewItem> items,
        ICollection<string> errors,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _temporaryFileService.ScanAsync(
                progress,
                cancellationToken);

            foreach (var candidate in result.Candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                items.Add(new CustomCleanPreviewItem(
                    CustomCleanCategory.TemporaryFiles,
                    candidate.FullPath,
                    candidate.SizeBytes,
                    candidate.LastWriteTimeUtc));
            }

            foreach (var error in result.Errors)
            {
                errors.Add(error);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            errors.Add($"Unable to preview current-user temporary files: {ex.Message}");
        }
    }

    private static bool IsSafeSkip(string error) =>
        error.StartsWith(
            "Blocked unsafe path:",
            StringComparison.OrdinalIgnoreCase) ||
        error.StartsWith(
            "Skipped reparse point:",
            StringComparison.OrdinalIgnoreCase) ||
        error.Contains(
            "read-only",
            StringComparison.OrdinalIgnoreCase);
}
