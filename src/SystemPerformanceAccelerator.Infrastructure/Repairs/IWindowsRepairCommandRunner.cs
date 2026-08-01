namespace SystemPerformanceAccelerator.Infrastructure.Repairs;

public interface IWindowsRepairCommandRunner
{
    Task<WindowsRepairCommandResult> RunAsync(
        WindowsRepairCommandRequest request,
        CancellationToken cancellationToken = default);
}
