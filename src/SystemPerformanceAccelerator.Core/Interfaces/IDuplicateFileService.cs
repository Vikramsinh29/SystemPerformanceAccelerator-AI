using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface IDuplicateFileService
{
    Task<DuplicateFileScanResult> ScanAsync(
        string rootPath,
        IProgress<DuplicateFileScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
