using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface IWindowsRepairAssessmentService
{
    Task<WindowsRepairAssessmentResult> AssessAsync(
        WindowsRepairAssessmentRequest request,
        Func<bool>? stopAfterCurrentCheck = null,
        IProgress<WindowsRepairAssessmentProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
