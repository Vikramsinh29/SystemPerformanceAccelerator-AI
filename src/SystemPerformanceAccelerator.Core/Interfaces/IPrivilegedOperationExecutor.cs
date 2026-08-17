using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface IPrivilegedOperationExecutor
{
    Task<PrivilegedOperationResult> ExecuteAsync(
        PrivilegedOperationRequest request,
        CancellationToken cancellationToken = default);
}
