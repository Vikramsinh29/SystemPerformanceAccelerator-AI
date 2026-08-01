using SystemPerformanceAccelerator.Infrastructure.Diagnostics;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class DiagnosticPathSanitizerTests
{
    [Fact]
    public void Sanitize_ReplacesUserProfileAndEmail()
    {
        var sanitizer = new DiagnosticPathSanitizer(
            userProfile: @"C:\Users\Alice",
            userName: "Alice");

        var result = sanitizer.Sanitize(
            @"User Alice failed at C:\Users\Alice\Documents\File.txt for alice@example.com.");

        Assert.Contains("%USERPROFILE%", result);
        Assert.Contains("<redacted-email>", result);
        Assert.DoesNotContain("Alice", result);
        Assert.DoesNotContain("alice@example.com", result);
    }

    [Fact]
    public void Sanitize_ReplacesAdditionalKnownPath()
    {
        var sanitizer = new DiagnosticPathSanitizer(
            userProfile: @"C:\Users\Alice",
            userName: "Alice",
            knownPaths: new Dictionary<string, string>
            {
                [@"D:\PrivateSupport"] = "%SUPPORTROOT%"
            });

        var result = sanitizer.Sanitize(
            @"D:\PrivateSupport\Logs\event.json");

        Assert.Equal(
            @"%SUPPORTROOT%\Logs\event.json",
            result);
    }

    [Fact]
    public void Sanitize_RedactsQuotedUnknownAbsolutePath()
    {
        var sanitizer = new DiagnosticPathSanitizer(
            userProfile: @"C:\Users\Alice",
            userName: "Alice");

        var result = sanitizer.Sanitize(
            "Could not open \"D:\\Clients\\Secret\\file.txt\".");

        Assert.Equal(
            "Could not open \"<redacted-path>\".",
            result);

        var unquotedResult = sanitizer.Sanitize(
            @"Failed at D:\Clients\Secret\file.txt because access was denied.");

        Assert.Equal(
            "Failed at <redacted-path>",
            unquotedResult);
    }

    [Fact]
    public void Sanitize_LeavesOrdinaryDiagnosticText()
    {
        var sanitizer = new DiagnosticPathSanitizer(
            userProfile: @"C:\Users\Alice",
            userName: "Alice");

        var result = sanitizer.Sanitize(
            "The scan was cancelled safely.");

        Assert.Equal(
            "The scan was cancelled safely.",
            result);
    }

    [Fact]
    public void Sanitize_SanitizesStackTraceSourcePath()
    {
        var sanitizer = new DiagnosticPathSanitizer(
            userProfile: @"C:\Users\Alice",
            userName: "Alice");

        var result = sanitizer.Sanitize(
            @"at Example.Run() in C:\Users\Alice\source\App.cs:line 42");

        Assert.Contains("%USERPROFILE%", result);
        Assert.DoesNotContain(
            @"C:\Users\Alice",
            result);
    }
}
