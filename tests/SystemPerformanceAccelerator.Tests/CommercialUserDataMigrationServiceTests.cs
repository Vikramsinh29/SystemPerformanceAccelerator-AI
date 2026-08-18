using SystemPerformanceAccelerator.Infrastructure.Services;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class CommercialUserDataMigrationServiceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(
            Path.GetTempPath(),
            "PCSPA-CommercialMigrationTests",
            Guid.NewGuid().ToString("N"));

    [Fact]
    public void CleanupLegacyBetaAccess_RemovesOnlyLegacyBetaDirectory()
    {
        var applicationRoot = Path.Combine(
            _root,
            "SystemPerformanceAccelerator");

        var betaDirectory = Path.Combine(
            applicationRoot,
            "beta-access");

        var repairDirectory = Path.Combine(
            applicationRoot,
            "repair-assessments",
            "records");

        var diagnosticsDirectory = Path.Combine(
            applicationRoot,
            "diagnostics");

        Directory.CreateDirectory(betaDirectory);
        Directory.CreateDirectory(repairDirectory);
        Directory.CreateDirectory(diagnosticsDirectory);

        File.WriteAllText(
            Path.Combine(betaDirectory, "installation.json"),
            "{}");

        File.WriteAllText(
            Path.Combine(applicationRoot, "settings.json"),
            "{}");

        File.WriteAllText(
            Path.Combine(repairDirectory, "ASSESS-TEST.json"),
            "{}");

        File.WriteAllText(
            Path.Combine(diagnosticsDirectory, "installation.json"),
            "{}");

        CommercialUserDataMigrationService
            .CleanupLegacyBetaAccess(_root);

        Assert.False(
            Directory.Exists(betaDirectory));

        Assert.True(
            File.Exists(
                Path.Combine(
                    applicationRoot,
                    "settings.json")));

        Assert.True(
            File.Exists(
                Path.Combine(
                    repairDirectory,
                    "ASSESS-TEST.json")));

        Assert.True(
            File.Exists(
                Path.Combine(
                    diagnosticsDirectory,
                    "installation.json")));
    }

    [Fact]
    public void CleanupLegacyBetaAccess_WhenLegacyDirectoryIsMissing_IsNoOp()
    {
        var applicationRoot = Path.Combine(
            _root,
            "SystemPerformanceAccelerator");

        Directory.CreateDirectory(applicationRoot);

        var settingsPath = Path.Combine(
            applicationRoot,
            "settings.json");

        File.WriteAllText(
            settingsPath,
            "{}");

        CommercialUserDataMigrationService
            .CleanupLegacyBetaAccess(_root);

        Assert.True(
            File.Exists(settingsPath));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(
                    _root,
                    recursive: true);
            }
        }
        catch
        {
        }
    }
}