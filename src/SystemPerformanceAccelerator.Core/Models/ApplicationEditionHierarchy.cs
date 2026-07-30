namespace SystemPerformanceAccelerator.Core.Models;

public static class ApplicationEditionHierarchy
{
    public static bool IsKnown(ApplicationEdition edition) => edition switch
    {
        ApplicationEdition.Trial or
        ApplicationEdition.Free or
        ApplicationEdition.Standard or
        ApplicationEdition.Pro or
        ApplicationEdition.Business => true,
        _ => false
    };

    public static bool MeetsOrExceeds(
        ApplicationEdition currentEdition,
        ApplicationEdition requiredEdition)
    {
        if (!IsKnown(currentEdition) || !IsKnown(requiredEdition))
        {
            return false;
        }

        if (requiredEdition == ApplicationEdition.Trial)
        {
            return currentEdition == ApplicationEdition.Trial;
        }

        if (currentEdition == ApplicationEdition.Trial)
        {
            return false;
        }

        return GetCommercialRank(currentEdition) >=
            GetCommercialRank(requiredEdition);
    }

    public static string GetDisplayName(ApplicationEdition edition) => edition switch
    {
        ApplicationEdition.Trial => "Trial",
        ApplicationEdition.Free => "Free",
        ApplicationEdition.Standard => "Standard",
        ApplicationEdition.Pro => "Pro",
        ApplicationEdition.Business => "Business",
        _ => "Unknown"
    };

    private static int GetCommercialRank(ApplicationEdition edition) => edition switch
    {
        ApplicationEdition.Free => 0,
        ApplicationEdition.Standard => 1,
        ApplicationEdition.Pro => 2,
        ApplicationEdition.Business => 3,
        _ => -1
    };
}
