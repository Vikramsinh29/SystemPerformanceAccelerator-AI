using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Repairs;

public sealed class WindowsRepairAssessmentReportExporter
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

    public async Task<string> ExportAsync(
        WindowsRepairAssessmentResult result,
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
                                "Read-only Windows Repair Assessment",
                            result.ReferenceId,
                            result.ApplicationVersion,
                            result.BuildIdentifier,
                            CreatedUtc = DateTimeOffset.UtcNow,
                            ContainsRepairActions = false,
                            ContainsPersonalFiles = false,
                            AutomaticUpload = false
                        },
                        cancellationToken);
                    await WriteJsonEntryAsync(
                        archive,
                        "assessment.json",
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
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
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

    private static string CreateReadme(
        WindowsRepairAssessmentResult result) =>
        $"""
        PC-SPA Read-only Windows Repair Assessment
        Reference: {result.ReferenceId}

        This package contains a sanitized local assessment report created by
        PC-SPA. It does not contain document contents, browser history,
        passwords, cookies, licence keys, machine serial numbers, or full
        personal paths.

        The assessment uses only these Microsoft read-only checks when selected:
        - DISM /Online /English /Cleanup-Image /CheckHealth
        - SFC /verifyonly

        No repair, component cleanup, CHKDSK operation, restart scheduling,
        registry modification, or automatic upload is performed.

        Result classification is conservative. An Inconclusive result means
        PC-SPA could not interpret the Microsoft output confidently. It does
        not mean Windows is healthy or damaged.

        Inspect assessment.json before sharing this ZIP.
        """;
}
