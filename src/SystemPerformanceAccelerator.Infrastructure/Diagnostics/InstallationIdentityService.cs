using System.Text;
using System.Text.Json;

namespace SystemPerformanceAccelerator.Infrastructure.Diagnostics;

public sealed class InstallationIdentityService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _syncRoot = new();

    public InstallationIdentityService(string identityPath)
    {
        if (string.IsNullOrWhiteSpace(identityPath))
        {
            throw new ArgumentException(
                "An installation identity path is required.",
                nameof(identityPath));
        }

        IdentityPath = Path.GetFullPath(identityPath);
    }

    public string IdentityPath { get; }

    public string? TryGet()
    {
        lock (_syncRoot)
        {
            return TryReadIdentity();
        }
    }

    public string GetOrCreate()
    {
        lock (_syncRoot)
        {
            var existing = TryReadIdentity();
            if (existing is not null)
            {
                return existing;
            }

            var identity = new InstallationIdentity(
                Guid.NewGuid().ToString("N"),
                DateTimeOffset.UtcNow);

            WriteIdentity(identity);
            return identity.Id;
        }
    }

    public void Reset()
    {
        lock (_syncRoot)
        {
            if (File.Exists(IdentityPath))
            {
                File.Delete(IdentityPath);
            }
        }
    }

    private string? TryReadIdentity()
    {
        if (!File.Exists(IdentityPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(IdentityPath);
            var identity = JsonSerializer.Deserialize<InstallationIdentity>(
                json,
                SerializerOptions);

            return identity is not null &&
                   Guid.TryParseExact(identity.Id, "N", out _)
                ? identity.Id
                : null;
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            JsonException or
            NotSupportedException or
            System.Security.SecurityException)
        {
            return null;
        }
    }

    private void WriteIdentity(InstallationIdentity identity)
    {
        var directory = Path.GetDirectoryName(IdentityPath)
            ?? throw new InvalidOperationException(
                "The installation identity path has no parent directory.");

        Directory.CreateDirectory(directory);

        var temporaryPath = IdentityPath + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(identity, SerializerOptions);
            File.WriteAllText(
                temporaryPath,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, IdentityPath, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException)
        {
        }
    }

    private sealed record InstallationIdentity(
        string Id,
        DateTimeOffset CreatedUtc);
}
