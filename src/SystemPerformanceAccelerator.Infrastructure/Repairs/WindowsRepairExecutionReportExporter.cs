using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Repairs;

public sealed class WindowsRepairExecutionReportExporter
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

    public async Task<string> ExportAsync(
        WindowsRepairExecutionResult result,
        string destinationZipPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationZipPath);

        var fullDestination = Path.GetFullPath(destinationZipPath);
        if (!string.Equals(
                Path.GetExtension(fullDestination),
                ".zip",
                StringComparison.OrdinalIgnoreCase))
        {
            fullDestination += ".zip";
        }

        var parent = Path.GetDirectoryName(fullDestination) ??
            throw new InvalidOperationException(
                "The report destination has no parent directory.");
        Directory.CreateDirectory(parent);

        var temporaryPath = fullDestination + ".tmp";
        try
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                using (var archive = new ZipArchive(
                    stream,
                    ZipArchiveMode.Create,
                    leaveOpen: true))
                {
                    await WriteTextEntryAsync(
                        archive,
                        "README.txt",
                        CreateReadme(result),
                        cancellationToken);
                    await WriteJsonEntryAsync(
                        archive,
                        "manifest.json",
                        new
                        {
                            Product = "PC-SPA",
                            ReportType =
                                "Guided Windows Repair Result",
                            result.ReferenceId,
                            result.AssessmentReferenceId,
                            result.ApplicationVersion,
                            result.BuildIdentifier,
                            CreatedUtc = DateTimeOffset.UtcNow,
                            ContainsRepairActions = true,
                            result.AutomaticRestartAttempted,
                            ContainsPersonalFiles = false,
                            AutomaticUpload = false
                        },
                        cancellationToken);
                    await WriteJsonEntryAsync(
                        archive,
                        "repair-result.json",
                        result,
                        cancellationToken);
                }

                await stream.FlushAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(
                temporaryPath,
                fullDestination,
                overwrite: true);
            return fullDestination;
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static async Task WriteTextEntryAsync(
        ZipArchive archive,
        string name,
        string content,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(
            name,
            CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024,
            leaveOpen: false);

        cancellationToken.ThrowIfCancellationRequested();
        await writer.WriteAsync(content).ConfigureAwait(false);
    }

    private static async Task WriteJsonEntryAsync<T>(
        ZipArchive archive,
        string name,
        T content,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(
            content,
            SerializerOptions);
        await WriteTextEntryAsync(
            archive,
            name,
            json,
            cancellationToken);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string CreateReadme(
        WindowsRepairExecutionResult result) =>
        $"""
        PC-SPA Guided Windows Repair Result
        Repair reference: {result.ReferenceId}
        Assessment reference: {result.AssessmentReferenceId}

        This package contains the latest sanitized guided-repair result saved
        locally by PC-SPA. It is different from the read-only Windows Repair
        Assessment report.

        The result can contain outcomes from this fixed guided sequence:
        - DISM RestoreHealth
        - SFC Scannow
        - DISM CheckHealth verification
        - SFC VerifyOnly verification

        PC-SPA did not automatically restart Windows unless the manifest and
        repair-result.json explicitly record AutomaticRestartAttempted as true.

        This package is not uploaded automatically. Inspect README.txt,
        manifest.json, and repair-result.json before sharing the ZIP.
        """;
}
