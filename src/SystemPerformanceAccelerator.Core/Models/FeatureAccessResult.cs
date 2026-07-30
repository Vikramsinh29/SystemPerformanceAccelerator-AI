namespace SystemPerformanceAccelerator.Core.Models;

public sealed record FeatureAccessResult(
    ApplicationFeature Feature,
    ApplicationEdition EffectiveEdition,
    FeatureAccessState State,
    ApplicationEdition? RequiredEdition,
    string Message)
{
    public bool IsAvailable =>
        State is FeatureAccessState.Available or FeatureAccessState.Trial;

    public bool IsVisible => State != FeatureAccessState.Hidden;
}
