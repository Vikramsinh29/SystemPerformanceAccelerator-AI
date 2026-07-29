using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface ICustomCleanService
{
    Task<CustomCleanPreviewResult> PreviewAsync(
        IReadOnlyCollection<CustomCleanCategory> categories,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);
}
