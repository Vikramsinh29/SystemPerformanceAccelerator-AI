using System.Globalization;
using System.Windows;
using SystemPerformanceAccelerator.Desktop.Converters;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class SubtractDoubleConverterTests
{
    private readonly SubtractDoubleConverter _converter = new();

    [Fact]
    public void Convert_SubtractsConfiguredMarginFromViewportHeight()
    {
        var result = _converter.Convert(
            900d,
            typeof(double),
            "34",
            CultureInfo.InvariantCulture);

        Assert.Equal(866d, result);
    }

    [Fact]
    public void Convert_ClampsSmallViewportToZero()
    {
        var result = _converter.Convert(
            20d,
            typeof(double),
            "34",
            CultureInfo.InvariantCulture);

        Assert.Equal(0d, result);
    }

    [Fact]
    public void Convert_RejectsInvalidInput()
    {
        var result = _converter.Convert(
            double.NaN,
            typeof(double),
            "34",
            CultureInfo.InvariantCulture);

        Assert.Same(DependencyProperty.UnsetValue, result);
    }
}
