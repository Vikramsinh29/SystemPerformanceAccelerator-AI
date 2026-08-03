namespace SystemPerformanceAccelerator.Core.Models;

public sealed record BetaAccessStatus(
    bool IsActive,
    string Status,
    string? EntitlementReference,
    DateTimeOffset? ActivatedUtc,
    DateTimeOffset? ExpiresUtc,
    int GracePeriodDays,
    string? Message)
{
    public static BetaAccessStatus NotActivated { get; } = new(
        false,
        "not_activated",
        null,
        null,
        null,
        0,
        "Enter a controlled-beta access code to activate this PC.");
}
