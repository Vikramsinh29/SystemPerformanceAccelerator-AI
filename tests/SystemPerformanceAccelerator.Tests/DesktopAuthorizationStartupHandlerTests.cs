using SystemPerformanceAccelerator.Desktop.Services;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class DesktopAuthorizationStartupHandlerTests
{
    [Fact]
    public void SelectAuthorizationActivation_ReturnsNullForNormalLaunch()
    {
        var result =
            DesktopAuthorizationStartupHandler
                .SelectAuthorizationActivation(
                    Array.Empty<string>());

        Assert.Null(result);
    }

    [Fact]
    public void SelectAuthorizationActivation_IgnoresOrdinaryArguments()
    {
        var result =
            DesktopAuthorizationStartupHandler
                .SelectAuthorizationActivation(
                    new[]
                    {
                        "--minimized",
                        "C:\\Temp\\file.txt"
                    });

        Assert.Null(result);
    }

    [Fact]
    public void SelectAuthorizationActivation_SelectsValidPcspaHandoff()
    {
        const string expected =
            "pcspa://authorize#code=one-time-code";

        var result =
            DesktopAuthorizationStartupHandler
                .SelectAuthorizationActivation(
                    new[]
                    {
                        "--ordinary",
                        expected,
                        "--later"
                    });

        Assert.Equal(
            expected,
            result);
    }

    [Fact]
    public void SelectAuthorizationActivation_RejectsWrongScheme()
    {
        var result =
            DesktopAuthorizationStartupHandler
                .SelectAuthorizationActivation(
                    new[]
                    {
                        "https://authorize#code=attacker"
                    });

        Assert.Null(result);
    }

    [Fact]
    public void SelectAuthorizationActivation_RejectsQueryBasedCode()
    {
        var result =
            DesktopAuthorizationStartupHandler
                .SelectAuthorizationActivation(
                    new[]
                    {
                        "pcspa://authorize?code=attacker"
                    });

        Assert.Null(result);
    }

    [Fact]
    public void SelectAuthorizationActivation_UsesFirstValidHandoffOnly()
    {
        const string first =
            "pcspa://authorize#code=first-code";

        const string second =
            "pcspa://authorize#code=second-code";

        var result =
            DesktopAuthorizationStartupHandler
                .SelectAuthorizationActivation(
                    new[]
                    {
                        first,
                        second
                    });

        Assert.Equal(
            first,
            result);
    }
}