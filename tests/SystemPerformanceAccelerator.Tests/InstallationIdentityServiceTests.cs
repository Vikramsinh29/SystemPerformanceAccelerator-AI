using SystemPerformanceAccelerator.Infrastructure.Diagnostics;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class InstallationIdentityServiceTests
{
    [Fact]
    public void TryGet_WhenIdentityDoesNotExist_ReturnsNull()
    {
        using var location = new TemporaryDiagnosticLocation();
        var service = new InstallationIdentityService(
            location.IdentityPath);

        Assert.Null(service.TryGet());
    }

    [Fact]
    public void GetOrCreate_PersistsStableRandomIdentity()
    {
        using var location = new TemporaryDiagnosticLocation();
        var service = new InstallationIdentityService(
            location.IdentityPath);

        var first = service.GetOrCreate();
        var second = service.GetOrCreate();

        Assert.Equal(first, second);
        Assert.True(Guid.TryParseExact(first, "N", out _));
        Assert.True(File.Exists(location.IdentityPath));
    }

    [Fact]
    public void GetOrCreate_WhenIdentityIsCorrupted_ReplacesIt()
    {
        using var location = new TemporaryDiagnosticLocation();
        Directory.CreateDirectory(location.Root);
        File.WriteAllText(
            location.IdentityPath,
            "{ invalid json");

        var service = new InstallationIdentityService(
            location.IdentityPath);
        var identity = service.GetOrCreate();

        Assert.True(Guid.TryParseExact(identity, "N", out _));
        Assert.Equal(identity, service.TryGet());
    }

    [Fact]
    public void Reset_RemovesStoredIdentity()
    {
        using var location = new TemporaryDiagnosticLocation();
        var service = new InstallationIdentityService(
            location.IdentityPath);
        service.GetOrCreate();

        service.Reset();

        Assert.Null(service.TryGet());
        Assert.False(File.Exists(location.IdentityPath));
    }

    private sealed class TemporaryDiagnosticLocation :
        IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(),
            $"pc-spa-identity-tests-{Guid.NewGuid():N}");

        public string IdentityPath =>
            Path.Combine(Root, "installation.json");

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, true);
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
