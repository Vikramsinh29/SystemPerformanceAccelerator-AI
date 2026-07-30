using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Configuration;
using SystemPerformanceAccelerator.Infrastructure.Services;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class FeatureAccessServiceTests
{
    [Theory]
    [InlineData(ApplicationEdition.Free)]
    [InlineData(ApplicationEdition.Standard)]
    [InlineData(ApplicationEdition.Pro)]
    [InlineData(ApplicationEdition.Business)]
    public void CurrentConfiguration_KeepsEveryFeatureAvailable(
        ApplicationEdition edition)
    {
        var service = new FeatureAccessService(
            edition,
            EditionFeatureEntitlements.Current);

        foreach (var definition in ApplicationFeatureCatalogue.All)
        {
            var access = service.GetAccess(definition.Id);

            Assert.Equal(FeatureAccessState.Available, access.State);
            Assert.True(access.IsAvailable);
            Assert.True(access.IsVisible);
        }
    }

    [Fact]
    public void CurrentConfiguration_ProvidesExplicitTrialAccessToEveryFeature()
    {
        var service = new FeatureAccessService(
            ApplicationEdition.Trial,
            EditionFeatureEntitlements.Current);

        foreach (var definition in ApplicationFeatureCatalogue.All)
        {
            var access = service.GetAccess(definition.Id);

            Assert.Equal(FeatureAccessState.Trial, access.State);
            Assert.True(access.IsAvailable);
            Assert.True(access.IsVisible);
        }
    }

    [Fact]
    public void LockedFeature_CanBeNavigatedToButCannotExecute()
    {
        var service = CreateService(
            ApplicationEdition.Free,
            new FeatureEntitlement(
                ApplicationFeature.SystemMonitor,
                ApplicationEdition.Pro,
                AvailableInTrial: false));
        var guard = new FeatureAccessGuard(service);

        var access = service.GetAccess(ApplicationFeature.SystemMonitor);

        Assert.Equal(FeatureAccessState.Locked, access.State);
        Assert.False(access.IsAvailable);
        Assert.True(access.IsVisible);
        Assert.Equal(ApplicationEdition.Pro, access.RequiredEdition);
        Assert.Equal("Available in Pro", access.Message);
        Assert.True(guard.CanAccess(
            ApplicationFeature.SystemMonitor,
            FeatureAccessRequirement.Navigate));
        Assert.False(guard.CanAccess(
            ApplicationFeature.SystemMonitor,
            FeatureAccessRequirement.Execute));
    }

    [Fact]
    public void TrialFeature_IsAvailableOnlyThroughExplicitTrialRule()
    {
        var enabledService = CreateService(
            ApplicationEdition.Trial,
            new FeatureEntitlement(
                ApplicationFeature.HealthCheck,
                ApplicationEdition.Pro,
                AvailableInTrial: true));
        var disabledService = CreateService(
            ApplicationEdition.Trial,
            new FeatureEntitlement(
                ApplicationFeature.HealthCheck,
                ApplicationEdition.Pro,
                AvailableInTrial: false));

        var enabled = enabledService.GetAccess(ApplicationFeature.HealthCheck);
        var disabled = disabledService.GetAccess(ApplicationFeature.HealthCheck);

        Assert.Equal(FeatureAccessState.Trial, enabled.State);
        Assert.True(enabled.IsAvailable);
        Assert.Equal(FeatureAccessState.Locked, disabled.State);
        Assert.False(disabled.IsAvailable);
    }

    [Fact]
    public void HiddenFeature_CannotBeNavigatedToOrExecuted()
    {
        var service = CreateService(
            ApplicationEdition.Free,
            new FeatureEntitlement(
                ApplicationFeature.CustomClean,
                ApplicationEdition.Business,
                AvailableInTrial: false,
                HideWhenUnavailable: true));
        var guard = new FeatureAccessGuard(service);

        var access = service.GetAccess(ApplicationFeature.CustomClean);

        Assert.Equal(FeatureAccessState.Hidden, access.State);
        Assert.False(access.IsAvailable);
        Assert.False(access.IsVisible);
        Assert.False(guard.CanAccess(
            ApplicationFeature.CustomClean,
            FeatureAccessRequirement.Navigate));
        Assert.False(guard.CanAccess(
            ApplicationFeature.CustomClean,
            FeatureAccessRequirement.Execute));
    }

    [Fact]
    public void UnknownFeature_FailsClosedWithoutThrowing()
    {
        var service = new FeatureAccessService(
            ApplicationEdition.Business,
            EditionFeatureEntitlements.Current);
        var guard = new FeatureAccessGuard(service);
        var unknownFeature = (ApplicationFeature)999;

        var exception = Record.Exception(() => service.GetAccess(unknownFeature));
        var access = service.GetAccess(unknownFeature);

        Assert.Null(exception);
        Assert.Equal(FeatureAccessState.Locked, access.State);
        Assert.False(access.IsAvailable);
        Assert.True(access.IsVisible);
        Assert.Null(access.RequiredEdition);
        Assert.False(guard.CanAccess(
            unknownFeature,
            FeatureAccessRequirement.Navigate));
        Assert.False(guard.CanAccess(
            unknownFeature,
            FeatureAccessRequirement.Execute));
    }

    [Fact]
    public void MissingEntitlement_FailsClosed()
    {
        var service = new FeatureAccessService(
            ApplicationEdition.Business,
            new Dictionary<ApplicationFeature, FeatureEntitlement>());

        var access = service.GetAccess(ApplicationFeature.Cleaner);

        Assert.Equal(FeatureAccessState.Locked, access.State);
        Assert.False(access.IsAvailable);
    }

    [Fact]
    public void UnknownAccessRequirement_FailsClosed()
    {
        var service = new FeatureAccessService(
            ApplicationEdition.Business,
            EditionFeatureEntitlements.Current);
        var guard = new FeatureAccessGuard(service);

        var allowed = guard.CanAccess(
            ApplicationFeature.Cleaner,
            (FeatureAccessRequirement)999);

        Assert.False(allowed);
    }

    private static FeatureAccessService CreateService(
        ApplicationEdition edition,
        FeatureEntitlement entitlement) =>
        new(
            edition,
            new Dictionary<ApplicationFeature, FeatureEntitlement>
            {
                [entitlement.Feature] = entitlement
            });
}
