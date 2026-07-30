using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface IFeatureAccessGuard
{
    ApplicationEdition EffectiveEdition { get; }
    bool IsDevelopmentOverrideActive { get; }
    FeatureAccessResult GetAccess(ApplicationFeature feature);
    bool CanAccess(
        ApplicationFeature feature,
        FeatureAccessRequirement requirement);
}
