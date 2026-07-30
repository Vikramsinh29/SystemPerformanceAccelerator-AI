using SystemPerformanceAccelerator.Core.Models;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class ApplicationEditionHierarchyTests
{
    [Theory]
    [InlineData(ApplicationEdition.Free, ApplicationEdition.Free, true)]
    [InlineData(ApplicationEdition.Free, ApplicationEdition.Standard, false)]
    [InlineData(ApplicationEdition.Standard, ApplicationEdition.Free, true)]
    [InlineData(ApplicationEdition.Standard, ApplicationEdition.Pro, false)]
    [InlineData(ApplicationEdition.Pro, ApplicationEdition.Standard, true)]
    [InlineData(ApplicationEdition.Pro, ApplicationEdition.Business, false)]
    [InlineData(ApplicationEdition.Business, ApplicationEdition.Pro, true)]
    [InlineData(ApplicationEdition.Business, ApplicationEdition.Business, true)]
    public void MeetsOrExceeds_UsesExplicitCommercialHierarchy(
        ApplicationEdition currentEdition,
        ApplicationEdition requiredEdition,
        bool expected)
    {
        var actual = ApplicationEditionHierarchy.MeetsOrExceeds(
            currentEdition,
            requiredEdition);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void MeetsOrExceeds_TreatsTrialAsExplicitNonCommercialEdition()
    {
        Assert.True(ApplicationEditionHierarchy.MeetsOrExceeds(
            ApplicationEdition.Trial,
            ApplicationEdition.Trial));
        Assert.False(ApplicationEditionHierarchy.MeetsOrExceeds(
            ApplicationEdition.Trial,
            ApplicationEdition.Free));
        Assert.False(ApplicationEditionHierarchy.MeetsOrExceeds(
            ApplicationEdition.Business,
            ApplicationEdition.Trial));
    }

    [Fact]
    public void MeetsOrExceeds_InvalidEditionFailsClosed()
    {
        var invalid = (ApplicationEdition)999;

        Assert.False(ApplicationEditionHierarchy.MeetsOrExceeds(
            invalid,
            ApplicationEdition.Free));
        Assert.False(ApplicationEditionHierarchy.MeetsOrExceeds(
            ApplicationEdition.Business,
            invalid));
    }
}
