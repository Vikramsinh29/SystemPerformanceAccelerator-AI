namespace SystemPerformanceAccelerator.Infrastructure.Services;

public static class CommercialUserDataMigrationService
{
    private const string ApplicationDataDirectoryName =
        "SystemPerformanceAccelerator";

    private const string LegacyBetaAccessDirectoryName =
        "beta-access";

    public static void CleanupLegacyBetaAccess(
        string? localApplicationDataRoot = null)
    {
        try
        {
            var localRoot = localApplicationDataRoot ??
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);

            if (string.IsNullOrWhiteSpace(localRoot))
            {
                return;
            }

            var applicationRoot = Path.Combine(
                localRoot,
                ApplicationDataDirectoryName);

            var legacyBetaAccessPath = Path.Combine(
                applicationRoot,
                LegacyBetaAccessDirectoryName);

            if (!Directory.Exists(legacyBetaAccessPath))
            {
                return;
            }

            Directory.Delete(
                legacyBetaAccessPath,
                recursive: true);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            // Legacy cleanup must never prevent PC-SPA from starting.
        }
    }
}