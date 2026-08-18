using System.Xml.Linq;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class ExecutionLevelManifestTests
{
    [Fact]
    public void DesktopRunsAsInvoker_AndHelperRequiresAdministrator()
    {
        var repoRoot = FindRepositoryRoot();
        var desktopManifest = ReadExecutionLevel(Path.Combine(
            repoRoot,
            "src",
            "SystemPerformanceAccelerator.Desktop",
            "app.manifest"));
        var helperManifest = ReadExecutionLevel(Path.Combine(
            repoRoot,
            "src",
            "SystemPerformanceAccelerator.PrivilegedHelper",
            "app.manifest"));

        Assert.Equal("asInvoker", desktopManifest);
        Assert.Equal("requireAdministrator", helperManifest);
    }

    private static string ReadExecutionLevel(string manifestPath)
    {
        var document = XDocument.Load(manifestPath);
        XNamespace asmV3 = "urn:schemas-microsoft-com:asm.v3";

        var requestedExecutionLevel = document
            .Descendants(asmV3 + "requestedExecutionLevel")
            .Single();

        return requestedExecutionLevel.Attribute("level")?.Value
            ?? throw new InvalidDataException(
                $"Execution level is missing from {manifestPath}.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                directory.FullName,
                "SystemPerformanceAccelerator.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Repository root could not be located from the test runtime directory.");
    }
}
