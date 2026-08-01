using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Configuration;

public static class EditionFeatureEntitlements
{
    public static ApplicationEdition DefaultEdition => ApplicationEdition.Free;

    private static readonly IReadOnlyDictionary<ApplicationFeature, FeatureEntitlement>
        CurrentEntitlements = new Dictionary<ApplicationFeature, FeatureEntitlement>
        {
            [ApplicationFeature.Cleaner] = AvailableToAll(ApplicationFeature.Cleaner),
            [ApplicationFeature.HealthCheck] = AvailableToAll(ApplicationFeature.HealthCheck),
            [ApplicationFeature.CustomClean] = AvailableToAll(ApplicationFeature.CustomClean),
            [ApplicationFeature.AutoCleanSchedule] = AvailableToAll(ApplicationFeature.AutoCleanSchedule),
            [ApplicationFeature.LargeFileFinder] = AvailableToAll(ApplicationFeature.LargeFileFinder),
            [ApplicationFeature.DuplicateFileFinder] = AvailableToAll(ApplicationFeature.DuplicateFileFinder),
            [ApplicationFeature.StartupManager] = AvailableToAll(ApplicationFeature.StartupManager),
            [ApplicationFeature.WindowsRepairAssessment] = AvailableToAll(ApplicationFeature.WindowsRepairAssessment),
            [ApplicationFeature.SystemMonitor] = AvailableToAll(ApplicationFeature.SystemMonitor),
            [ApplicationFeature.Settings] = AvailableToAll(ApplicationFeature.Settings)
        };

    public static IReadOnlyDictionary<ApplicationFeature, FeatureEntitlement> Current =>
        CurrentEntitlements;

    private static FeatureEntitlement AvailableToAll(ApplicationFeature feature) =>
        new(
            feature,
            ApplicationEdition.Free,
            AvailableInTrial: true);
}
