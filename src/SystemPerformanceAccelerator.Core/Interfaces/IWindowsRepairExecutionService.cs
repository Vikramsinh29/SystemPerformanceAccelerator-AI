using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface IWindowsRepairExecutionService
{
    WindowsRepairExecutionReadiness CheckReadiness(
        WindowsRepairAssessmentResult assessment);

    Task<WindowsRepairExecutionResult> ExecuteAsync(
        WindowsRepairAssessmentResult assessment,
        Func<bool>? stopAfterCurrentStep = null,
        IProgress<WindowsRepairExecutionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
