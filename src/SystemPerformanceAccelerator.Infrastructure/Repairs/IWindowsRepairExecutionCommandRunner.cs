namespace SystemPerformanceAccelerator.Infrastructure.Repairs;

public interface IWindowsRepairExecutionCommandRunner
{
    Task<WindowsRepairExecutionCommandResult> RunAsync(
        WindowsRepairExecutionCommandRequest request,
        CancellationToken cancellationToken = default);
}
