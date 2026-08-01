using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Desktop.Services;

public sealed class DisabledWindowsRepairAssessmentService :
    IWindowsRepairAssessmentService
{
    public Task<WindowsRepairAssessmentResult> AssessAsync(
        WindowsRepairAssessmentRequest request,
        Func<bool>? stopAfterCurrentCheck = null,
        IProgress<WindowsRepairAssessmentProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = DateTimeOffset.UtcNow;
        var environment = new WindowsRepairEnvironmentStatus(
            OperatingSystem.IsWindows(),
            false,
            Environment.OSVersion.VersionString,
            string.Empty,
            string.Empty,
            false,
            false,
            null,
            ["Windows Repair Assessment is unavailable in this context."]);

        return Task.FromResult(
            new WindowsRepairAssessmentResult(
                "ASSESS-UNAVAILABLE",
                now,
                now,
                "Unknown",
                "Unknown",
                environment,
                Array.Empty<WindowsRepairCheckResult>(),
                WindowsRepairAssessmentOutcome.Unsupported,
                false,
                environment.Issues));
    }
}
