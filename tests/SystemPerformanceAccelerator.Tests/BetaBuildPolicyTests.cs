using System.Reflection;
using SystemPerformanceAccelerator.Desktop.Services;

namespace SystemPerformanceAccelerator.Tests;

public sealed class BetaBuildPolicyTests
{
    private static readonly DateTimeOffset ReleaseUtc =
        DateTimeOffset.Parse("2026-08-07T00:00:00Z");

    [Fact]
    public void Evaluate_BeforeExpiry_RemainsAvailable()
    {
        var status = BetaBuildPolicy.Evaluate(
            ReleaseUtc,
            DateTimeOffset.Parse("2026-09-05T23:59:59Z"));

        Assert.False(status.IsExpired);
        Assert.Equal(
            DateTimeOffset.Parse("2026-09-06T00:00:00Z"),
            status.ExpiresUtc);
    }

    [Fact]
    public void Evaluate_AtExpiry_IsExpired()
    {
        var status = BetaBuildPolicy.Evaluate(
            ReleaseUtc,
            DateTimeOffset.Parse("2026-09-06T00:00:00Z"));

        Assert.True(status.IsExpired);
    }

    [Fact]
    public void CurrentBuild_EmbedsOfficialReleaseTimestamp()
    {
        var status = BetaBuildPolicy.Evaluate(
            typeof(BetaBuildPolicy).Assembly,
            ReleaseUtc);

        Assert.Equal(ReleaseUtc, status.ReleaseUtc);
        Assert.Equal(30, BetaBuildPolicy.ValidityDays);
    }

    [Fact]
    public void Evaluate_MissingReleaseMetadata_FailsClosed()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            BetaBuildPolicy.Evaluate(
                typeof(BetaBuildPolicyTests).Assembly,
                ReleaseUtc));

        Assert.Contains(
            BetaBuildPolicy.ReleaseMetadataKey,
            exception.Message,
            StringComparison.Ordinal);
    }
}
