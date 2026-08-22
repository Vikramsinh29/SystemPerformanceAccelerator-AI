using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SystemPerformanceAccelerator.Core.Interfaces;

namespace SystemPerformanceAccelerator.Infrastructure.Services;

public sealed class WindowsDesktopCredentialStore
    : IDesktopCredentialStore
{
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes(
            "PC-SPA.CommercialDesktopAuthorization.v1");

    private readonly string _credentialPath;

    public WindowsDesktopCredentialStore(
        string? credentialPath = null)
    {
        _credentialPath =
            credentialPath ??
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "PC-SPA",
                "commercial-auth.dat");
    }

    public async Task SaveAsync(
        string bearerToken,
        DateTimeOffset expiresUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var token = ValidateToken(bearerToken);

        if (expiresUtc <= DateTimeOffset.UtcNow)
        {
            throw new ArgumentException(
                "Credential expiration must be in the future.",
                nameof(expiresUtc));
        }

        var envelope =
            JsonSerializer.Serialize(
                new CredentialEnvelope(
                    token,
                    expiresUtc));

        var plaintext =
            Encoding.UTF8.GetBytes(envelope);

        byte[] protectedBytes;

        try
        {
            protectedBytes =
                ProtectedData.Protect(
                    plaintext,
                    Entropy,
                    DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                plaintext);
        }

        var directory =
            Path.GetDirectoryName(_credentialPath);

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                "Credential path does not have a valid directory.");
        }

        Directory.CreateDirectory(directory);

        await File.WriteAllBytesAsync(
            _credentialPath,
            protectedBytes,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<DesktopCredential?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(_credentialPath))
        {
            return null;
        }

        var protectedBytes =
            await File.ReadAllBytesAsync(
                _credentialPath,
                cancellationToken).ConfigureAwait(false);

        byte[] plaintext;

        try
        {
            plaintext =
                ProtectedData.Unprotect(
                    protectedBytes,
                    Entropy,
                    DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            return null;
        }

        try
        {
            var envelope =
                JsonSerializer.Deserialize<CredentialEnvelope>(
                    plaintext);

            if (
                envelope is null ||
                string.IsNullOrWhiteSpace(envelope.BearerToken))
            {
                return null;
            }

            var token =
                ValidateToken(envelope.BearerToken);

            if (envelope.ExpiresUtc <= DateTimeOffset.UtcNow)
            {
                return null;
            }

            return new DesktopCredential(
                token,
                envelope.ExpiresUtc);
        }
        catch (Exception exception)
            when (
                exception is JsonException ||
                exception is ArgumentException)
        {
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                plaintext);
        }
    }

    public Task ClearAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(_credentialPath))
        {
            File.Delete(_credentialPath);
        }

        return Task.CompletedTask;
    }

    private static string ValidateToken(
        string bearerToken)
    {
        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            throw new ArgumentException(
                "Bearer token is required.",
                nameof(bearerToken));
        }

        var token =
            bearerToken.Trim();

        if (
            token.Length > 4096 ||
            token.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "Bearer token is invalid.",
                nameof(bearerToken));
        }

        return token;
    }

    private sealed record CredentialEnvelope(
        string BearerToken,
        DateTimeOffset ExpiresUtc);
}