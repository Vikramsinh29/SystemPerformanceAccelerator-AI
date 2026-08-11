using System.Globalization;
using System.Reflection;

namespace SystemPerformanceAccelerator.Desktop.Services;

public sealed record BetaBuildStatus(
    DateTimeOffset ReleaseUtc,
    DateTimeOffset ExpiresUtc,
    bool IsExpired);

public static class BetaBuildPolicy
{
    public const int ValidityDays = 30;
    public const string ReleaseMetadataKey = "PCSPABetaReleaseUtc";

    public static BetaBuildStatus EvaluateCurrentBuild(
        DateTimeOffset? utcNow = null) =>
        Evaluate(
            typeof(BetaBuildPolicy).Assembly,
            utcNow ?? DateTimeOffset.UtcNow);

    public static BetaBuildStatus Evaluate(
        Assembly assembly,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var releaseText = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute =>
                string.Equals(
                    attribute.Key,
                    ReleaseMetadataKey,
                    StringComparison.Ordinal))
            ?.Value;

        if (!DateTimeOffset.TryParse(
                releaseText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal |
                DateTimeStyles.AdjustToUniversal,
                out var releaseUtc))
        {
            throw new InvalidOperationException(
                $"Beta build metadata '{ReleaseMetadataKey}' is missing or invalid.");
        }

        return Evaluate(releaseUtc, utcNow);
    }

    public static BetaBuildStatus Evaluate(
        DateTimeOffset releaseUtc,
        DateTimeOffset utcNow)
    {
        releaseUtc = releaseUtc.ToUniversalTime();
        utcNow = utcNow.ToUniversalTime();
        var expiresUtc = releaseUtc.AddDays(ValidityDays);

        return new BetaBuildStatus(
            releaseUtc,
            expiresUtc,
            utcNow >= expiresUtc);
    }
}
