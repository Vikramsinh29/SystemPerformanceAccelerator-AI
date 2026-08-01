using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Desktop.Services;

public sealed class DisabledWindowsRepairExecutionHistoryService :
    IWindowsRepairExecutionHistoryService
{
    public string ExecutionRoot => string.Empty;

    public Task SaveAsync(
        WindowsRepairExecutionResult result,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public WindowsRepairExecutionResult? LoadLatest() =>
        null;

    public IReadOnlyList<WindowsRepairExecutionResult> LoadRecent(
        int maximumCount = 20) =>
        Array.Empty<WindowsRepairExecutionResult>();

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
