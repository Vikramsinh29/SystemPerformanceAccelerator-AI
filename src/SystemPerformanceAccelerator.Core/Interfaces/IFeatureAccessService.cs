using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface IFeatureAccessService
{
    ApplicationEdition EffectiveEdition { get; }
    bool IsDevelopmentOverrideActive { get; }
    FeatureAccessResult GetAccess(ApplicationFeature feature);
}
