using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Configuration;
using SystemPerformanceAccelerator.Infrastructure.Services;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class DevelopmentEditionOverrideProviderTests
{
    [Theory]
    [InlineData("Trial", ApplicationEdition.Trial)]
    [InlineData("free", ApplicationEdition.Free)]
    [InlineData("STANDARD", ApplicationEdition.Standard)]
    [InlineData(" Pro ", ApplicationEdition.Pro)]
    [InlineData("Business", ApplicationEdition.Business)]
    public void GetOverride_ParsesEveryEditionWhenExplicitlyEnabled(
        string value,
        ApplicationEdition expected)
    {
        var provider = new DevelopmentEditionOverrideProvider(
            isEnabled: true,
            valueReader: () => value);

        Assert.Equal(expected, provider.GetOverride());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Enterprise")]
    [InlineData("999")]
    public void GetOverride_InvalidValueDefaultsSafely(string? value)
    {
        var provider = new DevelopmentEditionOverrideProvider(
            isEnabled: true,
            valueReader: () => value);

        Assert.Null(provider.GetOverride());
    }

    [Fact]
    public void GetOverride_IsDisabledByDefault()
    {
        var provider = new DevelopmentEditionOverrideProvider(
            valueReader: () => "Business");

        Assert.Null(provider.GetOverride());
    }

    [Fact]
    public void GetOverride_ValueReaderFailureDefaultsSafely()
    {
        var provider = new DevelopmentEditionOverrideProvider(
            isEnabled: true,
            valueReader: () => throw new InvalidOperationException());

        Assert.Null(provider.GetOverride());
    }

    [Fact]
    public void FeatureAccessService_UsesLocalDevelopmentOverrideWithoutChangingSettings()
    {
        var provider = new DevelopmentEditionOverrideProvider(
            isEnabled: true,
            valueReader: () => "Pro");
        var service = new FeatureAccessService(
            ApplicationEdition.Free,
            EditionFeatureEntitlements.Current,
            provider);

        Assert.Equal(ApplicationEdition.Pro, service.EffectiveEdition);
        Assert.True(service.IsDevelopmentOverrideActive);
    }

    [Fact]
    public void FeatureAccessService_UsesConfiguredEditionWhenOverrideIsAbsent()
    {
        var provider = new DevelopmentEditionOverrideProvider(
            isEnabled: true,
            valueReader: () => "invalid");
        var service = new FeatureAccessService(
            ApplicationEdition.Free,
            EditionFeatureEntitlements.Current,
            provider);

        Assert.Equal(ApplicationEdition.Free, service.EffectiveEdition);
        Assert.False(service.IsDevelopmentOverrideActive);
    }
}
