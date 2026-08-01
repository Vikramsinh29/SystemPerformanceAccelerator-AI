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

    public void DeleteHistory()
    {
    }
}
