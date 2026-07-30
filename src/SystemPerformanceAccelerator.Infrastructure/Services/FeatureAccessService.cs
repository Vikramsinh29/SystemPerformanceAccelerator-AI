using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Services;

public sealed class FeatureAccessService : IFeatureAccessService
{
    private readonly IReadOnlyDictionary<ApplicationFeature, FeatureEntitlement>
        _entitlements;

    public FeatureAccessService(
        ApplicationEdition configuredEdition,
        IReadOnlyDictionary<ApplicationFeature, FeatureEntitlement> entitlements,
        IDevelopmentEditionOverrideProvider? developmentOverrideProvider = null)
    {
        ArgumentNullException.ThrowIfNull(entitlements);

        _entitlements = entitlements;
        var developmentOverride = developmentOverrideProvider?.GetOverride();
        EffectiveEdition = developmentOverride ?? configuredEdition;
        IsDevelopmentOverrideActive = developmentOverride.HasValue;
    }

    public ApplicationEdition EffectiveEdition { get; }
    public bool IsDevelopmentOverrideActive { get; }

    public FeatureAccessResult GetAccess(ApplicationFeature feature)
    {
        if (!ApplicationFeatureCatalogue.TryGetDefinition(
                feature,
                out var definition))
        {
            return Unavailable(feature, "Feature is unavailable.");
        }

        if (!ApplicationEditionHierarchy.IsKnown(EffectiveEdition))
        {
            return Unavailable(
                feature,
                $"{definition.DisplayName} is unavailable.");
        }

        if (!_entitlements.TryGetValue(feature, out var entitlement) ||
            entitlement.Feature != feature ||
            !IsValidMinimumEdition(entitlement.MinimumEdition))
        {
            return Unavailable(
                feature,
                $"{definition.DisplayName} is unavailable.");
        }

        if (EffectiveEdition == ApplicationEdition.Trial)
        {
            if (entitlement.AvailableInTrial)
            {
                return new FeatureAccessResult(
                    feature,
                    EffectiveEdition,
                    FeatureAccessState.Trial,
                    entitlement.MinimumEdition,
                    $"{definition.DisplayName} is available during the trial.");
            }

            return NotEntitled(feature, entitlement);
        }

        if (ApplicationEditionHierarchy.MeetsOrExceeds(
                EffectiveEdition,
                entitlement.MinimumEdition))
        {
            return new FeatureAccessResult(
                feature,
                EffectiveEdition,
                FeatureAccessState.Available,
                entitlement.MinimumEdition,
                $"{definition.DisplayName} is available.");
        }

        return NotEntitled(feature, entitlement);
    }

    private FeatureAccessResult NotEntitled(
        ApplicationFeature feature,
        FeatureEntitlement entitlement)
    {
        var requiredEdition =
            ApplicationEditionHierarchy.GetDisplayName(
                entitlement.MinimumEdition);

        return new FeatureAccessResult(
            feature,
            EffectiveEdition,
            entitlement.HideWhenUnavailable
                ? FeatureAccessState.Hidden
                : FeatureAccessState.Locked,
            entitlement.MinimumEdition,
            $"Available in {requiredEdition}");
    }

    private FeatureAccessResult Unavailable(
        ApplicationFeature feature,
        string message) =>
        new(
            feature,
            EffectiveEdition,
            FeatureAccessState.Locked,
            null,
            message);

    private static bool IsValidMinimumEdition(
        ApplicationEdition edition) => edition switch
        {
            ApplicationEdition.Free or
            ApplicationEdition.Standard or
            ApplicationEdition.Pro or
            ApplicationEdition.Business => true,
            _ => false
        };
}
