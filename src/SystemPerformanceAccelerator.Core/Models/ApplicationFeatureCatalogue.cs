namespace SystemPerformanceAccelerator.Core.Models;

public sealed record ApplicationFeatureDefinition(
    ApplicationFeature Id,
    string DisplayName);

public static class ApplicationFeatureCatalogue
{
    private static readonly Dictionary<ApplicationFeature, ApplicationFeatureDefinition>
        Definitions = new()
        {
            [ApplicationFeature.Cleaner] = new(ApplicationFeature.Cleaner, "Cleaner"),
            [ApplicationFeature.HealthCheck] = new(ApplicationFeature.HealthCheck, "Health Check"),
            [ApplicationFeature.CustomClean] = new(ApplicationFeature.CustomClean, "Custom Clean"),
            [ApplicationFeature.AutoCleanSchedule] = new(ApplicationFeature.AutoCleanSchedule, "Auto Clean Schedule"),
            [ApplicationFeature.LargeFileFinder] = new(ApplicationFeature.LargeFileFinder, "Large File Finder"),
            [ApplicationFeature.DuplicateFileFinder] = new(ApplicationFeature.DuplicateFileFinder, "Duplicate File Finder"),
            [ApplicationFeature.StartupManager] = new(ApplicationFeature.StartupManager, "Startup Manager"),
            [ApplicationFeature.SystemMonitor] = new(ApplicationFeature.SystemMonitor, "System Monitor"),
            [ApplicationFeature.Settings] = new(ApplicationFeature.Settings, "Settings")
        };

    public static IReadOnlyCollection<ApplicationFeatureDefinition> All =>
        Definitions.Values;

    public static bool TryGetDefinition(
        ApplicationFeature feature,
        out ApplicationFeatureDefinition definition) =>
        Definitions.TryGetValue(feature, out definition!);
}
