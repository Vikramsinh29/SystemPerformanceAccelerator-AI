namespace SystemPerformanceAccelerator.Core.Models;

public sealed record FeatureEntitlement(
    ApplicationFeature Feature,
    ApplicationEdition MinimumEdition,
    bool AvailableInTrial,
    bool HideWhenUnavailable = false);
