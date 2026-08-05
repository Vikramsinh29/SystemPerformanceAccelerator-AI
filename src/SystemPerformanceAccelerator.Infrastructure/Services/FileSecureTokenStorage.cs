using System.Text.Json;
using System.Text.Json.Serialization;
using SystemPerformanceAccelerator.Core.Interfaces;

namespace SystemPerformanceAccelerator.Infrastructure.Services;

public sealed class FileSecureTokenStorage : ISecureTokenStorage
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition =
                JsonIgnoreCondition.WhenWritingNull
        };

    private readonly ICredentialProtector _credentialProtector;
    private readonly string _storagePath;

    public FileSecureTokenStorage(
        string storagePath,
        ICredentialProtector? credentialProtector = null)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
        {
            throw new ArgumentException(
                "A token storage path is required.",
                nameof(storagePath));
        }

        _storagePath = Path.GetFullPath(storagePath);
        _credentialProtector = credentialProtector ??
            new WindowsDataProtectionCredentialProtector();
    }

    public Task<string?> GetSessionTokenAsync(
        CancellationToken cancellationToken = default) =>
        GetTokenAsync(TokenKind.Session, cancellationToken);

    public Task StoreSessionTokenAsync(
        string sessionToken,
        CancellationToken cancellationToken = default) =>
        StoreTokenAsync(TokenKind.Session, sessionToken, cancellationToken);

    public Task ClearSessionTokenAsync(
        CancellationToken cancellationToken = default) =>
        ClearTokenAsync(TokenKind.Session, cancellationToken);

    public Task<string?> GetLicenseTokenAsync(
        CancellationToken cancellationToken = default) =>
        GetTokenAsync(TokenKind.License, cancellationToken);

    public Task StoreLicenseTokenAsync(
        string licenseToken,
        CancellationToken cancellationToken = default) =>
        StoreTokenAsync(TokenKind.License, licenseToken, cancellationToken);

    public Task ClearLicenseTokenAsync(
        CancellationToken cancellationToken = default) =>
        ClearTokenAsync(TokenKind.License, cancellationToken);

    private Task<string?> GetTokenAsync(
        TokenKind tokenKind,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var document = LoadDocument();
        return Task.FromResult(
            document?.Tokens
                .FirstOrDefault(item => item.Kind == tokenKind)
                ?.Value);
    }

    private Task StoreTokenAsync(
        TokenKind tokenKind,
        string token,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException(
                "A non-empty token is required.",
                nameof(token));
        }

        var document = LoadDocument() ?? new TokenDocument(1, []);
        var tokens = document.Tokens
            .Where(item => item.Kind != tokenKind)
            .Append(new StoredToken(tokenKind, token))
            .ToArray();
        SaveDocument(document with { Tokens = tokens });
        return Task.CompletedTask;
    }

    private Task ClearTokenAsync(
        TokenKind tokenKind,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var document = LoadDocument();
        if (document is null)
        {
            return Task.CompletedTask;
        }

        var tokens = document.Tokens
            .Where(item => item.Kind != tokenKind)
            .ToArray();
        if (tokens.Length == 0)
        {
            TryDelete(_storagePath);
            return Task.CompletedTask;
        }

        SaveDocument(document with { Tokens = tokens });
        return Task.CompletedTask;
    }

    private TokenDocument? LoadDocument()
    {
        if (!File.Exists(_storagePath))
        {
            return null;
        }

        var protectedBytes = File.ReadAllBytes(_storagePath);
        var plaintext = _credentialProtector.Unprotect(protectedBytes);
        try
        {
            return JsonSerializer.Deserialize<TokenDocument>(
                plaintext,
                SerializerOptions);
        }
        finally
        {
            Array.Clear(plaintext);
            Array.Clear(protectedBytes);
        }
    }

    private void SaveDocument(TokenDocument document)
    {
        var directory = Path.GetDirectoryName(_storagePath)
            ?? throw new InvalidOperationException(
                "The token storage path has no parent directory.");
        Directory.CreateDirectory(directory);

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(
            document,
            SerializerOptions);
        byte[]? protectedBytes = null;
        var temporaryPath = _storagePath + ".tmp";
        try
        {
            protectedBytes = _credentialProtector.Protect(plaintext);
            File.WriteAllBytes(temporaryPath, protectedBytes);
            File.Move(temporaryPath, _storagePath, overwrite: true);
        }
        finally
        {
            Array.Clear(plaintext);
            if (protectedBytes is not null)
            {
                Array.Clear(protectedBytes);
            }

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

    private enum TokenKind
    {
        Session = 0,
        License = 1
    }

    private sealed record StoredToken(
        TokenKind Kind,
        string Value);

    private sealed record TokenDocument(
        int SchemaVersion,
        IReadOnlyList<StoredToken> Tokens);
}
