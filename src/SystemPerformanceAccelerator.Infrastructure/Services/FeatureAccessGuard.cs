using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Services;

public sealed class FeatureAccessGuard : IFeatureAccessGuard
{
    private readonly IFeatureAccessService _featureAccessService;

    public FeatureAccessGuard(IFeatureAccessService featureAccessService)
    {
        _featureAccessService = featureAccessService ??
            throw new ArgumentNullException(nameof(featureAccessService));
    }

    public ApplicationEdition EffectiveEdition =>
        _featureAccessService.EffectiveEdition;

    public bool IsDevelopmentOverrideActive =>
        _featureAccessService.IsDevelopmentOverrideActive;

    public FeatureAccessResult GetAccess(ApplicationFeature feature) =>
        _featureAccessService.GetAccess(feature);

    public bool CanAccess(
        ApplicationFeature feature,
        FeatureAccessRequirement requirement)
    {
        if (!ApplicationFeatureCatalogue.TryGetDefinition(feature, out _))
        {
            return false;
        }

        var access = GetAccess(feature);

        return requirement switch
        {
            FeatureAccessRequirement.Navigate => access.IsVisible,
            FeatureAccessRequirement.Execute => access.IsAvailable,
            _ => false
        };
    }
}
