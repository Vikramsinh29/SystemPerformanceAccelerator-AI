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
}
