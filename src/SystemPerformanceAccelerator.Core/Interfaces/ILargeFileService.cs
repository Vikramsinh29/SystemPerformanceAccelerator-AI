using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface ILargeFileService
{
    Task<LargeFileScanResult> ScanAsync(
        string rootPath,
        long minimumSizeBytes,
        IProgress<LargeFileScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
