using System.Diagnostics;

namespace SystemPerformanceAccelerator.Infrastructure.Repairs;

public sealed class WindowsRepairExecutionCommandRunner :
    IWindowsRepairExecutionCommandRunner
{
    public const int MaximumCapturedCharacters = 32_768;

    private readonly Func<DateTimeOffset> _utcNow;

    public WindowsRepairExecutionCommandRunner(
        Func<DateTimeOffset>? utcNow = null)
    {
        _utcNow = utcNow ??
            (() => DateTimeOffset.UtcNow);
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

        if (!File.Exists(request.ExecutablePath))
        {
            return FailedBeforeStart(
                startedUtc,
                "The required Microsoft Windows executable was not found.");
        }

        try
        {
            using var process = new Process
            {
                StartInfo = request.CreateStartInfo(),
                EnableRaisingEvents = true
            };

            if (!process.Start())
            {
                return FailedBeforeStart(
                    startedUtc,
                    "Windows did not start the guided repair command.");
            }

            var outputTask =
                process.StandardOutput.ReadToEndAsync();
            var errorTask =
                process.StandardError.ReadToEndAsync();

            // Once a Microsoft repair process starts, PC-SPA
            // intentionally lets it finish normally.
            await process.WaitForExitAsync(
                    CancellationToken.None)
                .ConfigureAwait(false);

            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            var finishedUtc =
                _utcNow().ToUniversalTime();

            return new WindowsRepairExecutionCommandResult(
                Started: true,
                process.ExitCode,
                startedUtc,
                finishedUtc,
                Limit(output),
                Limit(error),
                string.Empty);
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or
            System.ComponentModel.Win32Exception or
            IOException or
            UnauthorizedAccessException)
        {
            return FailedBeforeStart(
                startedUtc,
                ex.Message);
        }
    }

    private WindowsRepairExecutionCommandResult
        FailedBeforeStart(
            DateTimeOffset startedUtc,
            string failure) =>
        new(
            Started: false,
            ExitCode: null,
            startedUtc,
            _utcNow().ToUniversalTime(),
            string.Empty,
            string.Empty,
            Limit(failure));

    private static string Limit(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.Length <= MaximumCapturedCharacters)
        {
            return value;
        }

        const string suffix =
            "\n<output truncated by PC-SPA>";
        var permittedLength =
            MaximumCapturedCharacters - suffix.Length;

        return value[..Math.Max(0, permittedLength)] +
            suffix;
    }
}
