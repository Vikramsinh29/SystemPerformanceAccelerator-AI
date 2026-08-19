using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Repairs;

public sealed class PrivilegedWindowsRepairAssessmentCommandRunner :
    IWindowsRepairCommandRunner
{
    public const string CheckHealthOperation =
        "windows-repair-assess-check-health";

    public const string VerifyProtectedFilesOperation =
        "windows-repair-assess-verify-protected-files";

    private readonly string _helperPath;

    public PrivilegedWindowsRepairAssessmentCommandRunner()
        : this(
            Path.Combine(
                AppContext.BaseDirectory,
                "PC-SPA.PrivilegedHelper.exe"))
    {
    }

    internal PrivilegedWindowsRepairAssessmentCommandRunner(
        string helperPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(helperPath);
        _helperPath = Path.GetFullPath(helperPath);
    }

    public async Task<WindowsRepairCommandResult> RunAsync(
        WindowsRepairCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var startedUtc = DateTimeOffset.UtcNow;

        if (!request.IsStrictlyReadOnly)
        {
            return Failed(
                startedUtc,
                "The request was not an approved read-only Windows assessment.");
        }

        if (!File.Exists(_helperPath))
        {
            return Failed(
                startedUtc,
                "The PC-SPA privileged helper is unavailable.");
        }

        var operation = request.Check switch
        {
            WindowsRepairAssessmentCheck.ComponentStoreCheckHealth =>
                CheckHealthOperation,

            WindowsRepairAssessmentCheck.ProtectedSystemFilesVerifyOnly =>
                VerifyProtectedFilesOperation,

            _ => null
        };

        if (operation is null)
        {
            return Failed(
                startedUtc,
                "The Windows assessment operation is not supported.");
        }

        var token = Guid.NewGuid().ToString("N");
        var resultPath =
            PrivilegedWindowsRepairAssessmentExchange
                .GetResultPath(token);

        Directory.CreateDirectory(
            Path.GetDirectoryName(resultPath)!);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _helperPath,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory =
                    Path.GetDirectoryName(_helperPath) ??
                    AppContext.BaseDirectory
            };

            startInfo.ArgumentList.Add(operation);
            startInfo.ArgumentList.Add(token);

            using var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            if (!process.Start())
            {
                return Failed(
                    startedUtc,
                    "Windows did not start the privileged assessment helper.");
            }

            // Once Microsoft DISM/SFC starts, PC-SPA lets it finish normally.
            await process
                .WaitForExitAsync(CancellationToken.None)
                .ConfigureAwait(false);

            if (!File.Exists(resultPath))
            {
                return Failed(
                    startedUtc,
                    process.ExitCode == 1223
                        ? "Administrator permission was cancelled."
                        : $"The privileged assessment helper exited with code {process.ExitCode} without returning evidence.");
            }

            var json =
                await File.ReadAllTextAsync(
                    resultPath,
                    CancellationToken.None)
                    .ConfigureAwait(false);

            var result =
                JsonSerializer.Deserialize<WindowsRepairCommandResult>(
                    json);

            return result ??
                Failed(
                    startedUtc,
                    "The privileged assessment result could not be read.");
        }
        catch (Win32Exception ex)
            when (ex.NativeErrorCode == 1223)
        {
            return Failed(
                startedUtc,
                "Administrator permission was cancelled. No Windows assessment was started.");
        }
        catch (Exception ex) when (
            ex is Win32Exception or
            IOException or
            UnauthorizedAccessException or
            JsonException)
        {
            return Failed(
                startedUtc,
                ex.Message);
        }
        finally
        {
            try
            {
                if (File.Exists(resultPath))
                {
                    File.Delete(resultPath);
                }
            }
            catch
            {
                // Evidence exchange cleanup is best-effort.
            }
        }
    }

    private static WindowsRepairCommandResult Failed(
        DateTimeOffset startedUtc,
        string reason) =>
        new(
            Started: false,
            ExitCode: null,
            startedUtc,
            DateTimeOffset.UtcNow,
            string.Empty,
            string.Empty,
            reason);
}

public static class PrivilegedWindowsRepairAssessmentExchange
{
    private const string FolderName =
        "privileged-assessment-results";

    public static string GetResultPath(string token)
    {
        if (
            string.IsNullOrWhiteSpace(token) ||
            token.Length != 32 ||
            !Guid.TryParseExact(token, "N", out _))
        {
            throw new ArgumentException(
                "Invalid privileged assessment exchange token.",
                nameof(token));
        }

        var localData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(localData))
        {
            throw new InvalidOperationException(
                "Local application data is unavailable.");
        }

        return Path.Combine(
            localData,
            "SystemPerformanceAccelerator",
            FolderName,
            token + ".json");
    }
}