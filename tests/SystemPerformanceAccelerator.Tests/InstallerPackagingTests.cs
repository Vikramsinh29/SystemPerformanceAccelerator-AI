using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class InstallerPackagingTests
{
    [Fact]
    public void InstallerDefinition_UsesPerMachineNoRestartContract()
    {
        var definition = File.ReadAllText(FindRepositoryFile(
            "installer",
            "PC-SPA.iss"));

        Assert.Contains("DefaultDirName={autopf}\\PC-SPA", definition);
        Assert.Contains("PrivilegesRequired=admin", definition);
        Assert.Contains("ArchitecturesAllowed=x64compatible", definition);
        Assert.Contains("RestartApplications=no", definition);
        Assert.Contains("RestartIfNeededByRun=no", definition);
        Assert.DoesNotContain("[UninstallDelete]", definition);
    }

    [Fact]
    public void InstallerDefinition_CreatesExpectedShortcutsWithoutSilentLaunch()
    {
        var definition = File.ReadAllText(FindRepositoryFile(
            "installer",
            "PC-SPA.iss"));

        Assert.Contains("Name: \"{group}\\PC-SPA\"", definition);
        Assert.Contains("Name: \"desktopicon\"", definition);
        Assert.DoesNotContain("Flags: unchecked", definition);
        Assert.Contains("Verb: \"runas\"; Flags: nowait postinstall skipifsilent shellexec", definition);
    }

    [Fact]
    public void InstallerPublishScript_ReusesVerifiedPublishAndReportsIntegrity()
    {
        var script = File.ReadAllText(FindRepositoryFile(
            "scripts",
            "Publish-Windows-x64-Installer.ps1"));

        Assert.Contains("Publish-Windows-x64.ps1", script);
        Assert.Contains("PC-SPA-1.0.0-win-x64-setup.exe", script);
        Assert.Contains("INNO_SETUP_COMPILER", script);
        Assert.Contains("Programs\\Inno Setup 6\\ISCC.exe", script);
        Assert.Contains("Get-FileHash", script);
        Assert.Contains("Get-AuthenticodeSignature", script);
        Assert.Contains("SkipDesktopCopy", script);
        Assert.Contains("$portableArguments.SkipDesktopCopy = $true", script);
        Assert.DoesNotContain("Invoke-WebRequest", script);
    }

    private static string FindRepositoryFile(params string[] relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativePath]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repository file '{Path.Combine(relativePath)}'.");
    }
}
