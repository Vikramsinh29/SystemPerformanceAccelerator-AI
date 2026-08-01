using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Desktop.Services;

public sealed class DisabledWindowsRepairAssessmentHistoryService :
    IWindowsRepairAssessmentHistoryService
{
    public string AssessmentRoot => string.Empty;

    public Task SaveAsync(
        WindowsRepairAssessmentResult result,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public WindowsRepairAssessmentResult? LoadLatest() => null;

    public Task<string?> ExportLatestAsync(
        string destinationZipPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(null);
    }

    public void DeleteHistory()
    {
    }
}
