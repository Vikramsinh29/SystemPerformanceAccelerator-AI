using SystemPerformanceAccelerator.Infrastructure.Services;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class FileSecureTokenStorageTests
{
    [Fact]
    public async Task StoreAndLoadTokens_PersistsEncryptedValues()
    {
        using var location = new TemporaryLocation();
        var storage = new FileSecureTokenStorage(
            location.TokenPath,
            new PrefixCredentialProtector());

        await storage.StoreSessionTokenAsync("session-1");
        await storage.StoreLicenseTokenAsync("license-1");

        Assert.Equal("session-1", await storage.GetSessionTokenAsync());
        Assert.Equal("license-1", await storage.GetLicenseTokenAsync());
        Assert.DoesNotContain("session-1", File.ReadAllText(location.TokenPath));
        Assert.DoesNotContain("license-1", File.ReadAllText(location.TokenPath));
    }

    [Fact]
    public async Task ClearLicenseTokenAsync_RemovesOnlyLicenseToken()
    {
        using var location = new TemporaryLocation();
        var storage = new FileSecureTokenStorage(
            location.TokenPath,
            new PrefixCredentialProtector());
        await storage.StoreSessionTokenAsync("session-1");
        await storage.StoreLicenseTokenAsync("license-1");

        await storage.ClearLicenseTokenAsync();

        Assert.Equal("session-1", await storage.GetSessionTokenAsync());
        Assert.Null(await storage.GetLicenseTokenAsync());
    }

    private sealed class PrefixCredentialProtector : ICredentialProtector
    {
        private static readonly byte[] Prefix = "protected:"u8.ToArray();
        private const byte Mask = 0x5A;

        public byte[] Protect(byte[] plaintext)
        {
            var protectedBytes = new byte[Prefix.Length + plaintext.Length];
            Prefix.CopyTo(protectedBytes, 0);
            for (var index = 0; index < plaintext.Length; index++)
            {
                protectedBytes[Prefix.Length + index] =
                    (byte)(plaintext[index] ^ Mask);
            }

            return protectedBytes;
        }

        public byte[] Unprotect(byte[] protectedData)
        {
            var plaintext = protectedData.AsSpan(Prefix.Length).ToArray();
            for (var index = 0; index < plaintext.Length; index++)
            {
                plaintext[index] = (byte)(plaintext[index] ^ Mask);
            }

            return plaintext;
        }
    }

    private sealed class TemporaryLocation : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(),
            $"pc-spa-token-storage-tests-{Guid.NewGuid():N}");

        public string TokenPath => Path.Combine(Root, "tokens.dat");

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
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
}
