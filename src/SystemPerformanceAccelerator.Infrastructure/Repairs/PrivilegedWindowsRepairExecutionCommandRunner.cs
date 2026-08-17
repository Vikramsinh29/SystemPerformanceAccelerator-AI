using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Repairs;

public sealed class PrivilegedWindowsRepairExecutionCommandRunner :
    IWindowsRepairExecutionCommandRunner
{
    private readonly IPrivilegedOperationExecutor _executor;
    private readonly Func<DateTimeOffset> _utcNow;

    public PrivilegedWindowsRepairExecutionCommandRunner(
        IPrivilegedOperationExecutor executor,
        Func<DateTimeOffset>? utcNow = null)
    {
        _executor = executor ??
            throw new ArgumentNullException(nameof(executor));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<WindowsRepairExecutionCommandResult> RunAsync(
        WindowsRepairExecutionCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var startedUtc = _utcNow().ToUniversalTime();

        if (!request.IsApprovedGuidedRepairCommand)
        {
            return FailedBeforeStart(
                startedUtc,
                "The command request was blocked because it was not an approved guided Windows repair command.");
        }

        var privilegedRequest = request.Step switch
        {
            WindowsRepairExecutionStep.ComponentStoreRepair =>
                PrivilegedOperationRequest.CreateWindowsRepairRestoreHealth(),
            WindowsRepairExecutionStep.ProtectedSystemFilesRepair =>
                PrivilegedOperationRequest.CreateWindowsRepairScanProtectedFiles(),
            _ => null
        };

        if (privilegedRequest is null)
        {
            return FailedBeforeStart(
                startedUtc,
                "The repair step is not connected to the privileged-operation boundary.");
        }

        var result = await _executor
            .ExecuteAsync(privilegedRequest, cancellationToken)
            .ConfigureAwait(false);

        var finishedUtc = _utcNow().ToUniversalTime();

        if (!result.Started)
        {
            return new WindowsRepairExecutionCommandResult(
                Started: false,
                ExitCode: null,
                startedUtc,
                finishedUtc,
                string.Empty,
                string.Empty,
                result.Message);
        }

        return new WindowsRepairExecutionCommandResult(
            Started: true,
            ExitCode: result.Succeeded ? 0 : 1,
            startedUtc,
            finishedUtc,
            result.Succeeded ? result.Message : string.Empty,
            result.Succeeded ? string.Empty : result.Message,
            string.Empty);
    }

    private WindowsRepairExecutionCommandResult FailedBeforeStart(
        DateTimeOffset startedUtc,
        string failure) =>
        new(
            Started: false,
            ExitCode: null,
            startedUtc,
            _utcNow().ToUniversalTime(),
            string.Empty,
            string.Empty,
            failure);
}
