using SystemPerformanceAccelerator.Infrastructure.Services;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class WindowsDeviceIdentityProviderTests
{
    [Fact]
    public async Task GetDeviceIdAsync_IsStableForSameInputs()
    {
        using var location = new TemporaryLocation();
        var provider = new WindowsDeviceIdentityProvider(
            location.IdentityPath,
            () => "machine-guid-123",
            () => "user-sid-456");

        var first = await provider.GetDeviceIdAsync();
        var second = await provider.GetDeviceIdAsync();

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.DoesNotContain("machine-guid-123", first, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user-sid-456", first, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetDeviceIdAsync_FallsBackToPersistedValueWhenInputsUnavailable()
    {
        using var location = new TemporaryLocation();
        var provider = new WindowsDeviceIdentityProvider(
            location.IdentityPath,
            () => null,
            () => null);

        var first = await provider.GetDeviceIdAsync();
        var second = await provider.GetDeviceIdAsync();

        Assert.Equal(first, second);
        Assert.True(File.Exists(location.IdentityPath));
    }

    private sealed class TemporaryLocation : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(),
            $"pc-spa-device-identity-tests-{Guid.NewGuid():N}");

        public string IdentityPath => Path.Combine(Root, "device-id.txt");

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
