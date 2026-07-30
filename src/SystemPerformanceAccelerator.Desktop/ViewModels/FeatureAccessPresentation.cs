using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Desktop.ViewModels;

public sealed class FeatureAccessPresentation
{
    public FeatureAccessPresentation(FeatureAccessResult access)
    {
        Access = access ?? throw new ArgumentNullException(nameof(access));
    }

    public FeatureAccessResult Access { get; }
    public bool IsAvailable => Access.IsAvailable;
    public bool IsVisible => Access.IsVisible;
    public bool IsBadgeVisible => Access.State != FeatureAccessState.Available;
    public string Message => Access.Message;

    public string BadgeText => Access.State switch
    {
        FeatureAccessState.Trial => "TRIAL",
        FeatureAccessState.Locked when Access.RequiredEdition.HasValue =>
            Access.RequiredEdition.Value switch
            {
                ApplicationEdition.Free => "FREE",
                ApplicationEdition.Standard => "STD",
                ApplicationEdition.Pro => "PRO",
                ApplicationEdition.Business => "BUS",
                _ => "LOCKED"
            },
        FeatureAccessState.Locked => "LOCKED",
        FeatureAccessState.Hidden => "HIDDEN",
        _ => string.Empty
    };
}
