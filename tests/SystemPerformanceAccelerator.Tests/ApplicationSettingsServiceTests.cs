using System.Text.Json;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Services;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class ApplicationSettingsServiceTests
{
    [Fact]
    public void Load_WhenFileDoesNotExist_ReturnsSafeDefaults()
    {
        using var location = new TemporarySettingsLocation();
        var service = new ApplicationSettingsService(
            location.SettingsPath);

        var result = service.Load();

        Assert.Equal(ApplicationSettings.Default, result.Settings);
        Assert.False(result.HasWarning);
        Assert.False(File.Exists(location.SettingsPath));
    }

    [Fact]
    public void SaveAndLoad_RoundTripsSupportedSettings()
    {
        using var location = new TemporarySettingsLocation();
        var service = new ApplicationSettingsService(
            location.SettingsPath);
        var expected = new ApplicationSettings(
            ApplicationTheme.Dark,
            true,
            512,
            4);

        service.Save(expected);
        var result = service.Load();

        Assert.Equal(expected, result.Settings);
        Assert.False(result.HasWarning);
        Assert.True(File.Exists(location.SettingsPath));
    }

    [Fact]
    public void SaveAndLoad_RoundTripsDiagnosticPreferences()
    {
        using var location = new TemporarySettingsLocation();
        var service = new ApplicationSettingsService(
            location.SettingsPath);
        var expected = new ApplicationSettings(
            ApplicationTheme.Light,
            true,
            256,
            2)
        {
            LocalDiagnosticsEnabled = true,
            IncludeHardwareSummaryInDiagnosticExport = true
        };

        service.Save(expected);
        var result = service.Load();

        Assert.Equal(expected, result.Settings);
        Assert.True(result.Settings.LocalDiagnosticsEnabled);
        Assert.True(
            result.Settings
                .IncludeHardwareSummaryInDiagnosticExport);
        Assert.False(result.HasWarning);
    }

    [Fact]
    public void Load_LegacyJsonWithoutDiagnosticFields_DefaultsToDisabled()
    {
        using var location = new TemporarySettingsLocation();
        Directory.CreateDirectory(
            Path.GetDirectoryName(location.SettingsPath)!);
        File.WriteAllText(
            location.SettingsPath,
            "{\n" +
            "  \"Theme\": \"Dark\",\n" +
            "  \"ConfirmBeforeCleanup\": true,\n" +
            "  \"LargeFileMinimumSizeMb\": 300,\n" +
            "  \"SystemMonitorRefreshIntervalSeconds\": 3\n" +
            "}");

        var service = new ApplicationSettingsService(
            location.SettingsPath);
        var result = service.Load();

        Assert.Equal(ApplicationTheme.Dark, result.Settings.Theme);
        Assert.False(
            result.Settings.LocalDiagnosticsEnabled);
        Assert.False(
            result.Settings
                .IncludeHardwareSummaryInDiagnosticExport);
        Assert.False(result.HasWarning);
    }

    [Fact]
    public void Load_WhenJsonIsMalformed_ReturnsDefaultsWithWarning()
    {
        using var location = new TemporarySettingsLocation();
        Directory.CreateDirectory(
            Path.GetDirectoryName(location.SettingsPath)!);
        File.WriteAllText(
            location.SettingsPath,
            "{ invalid json");
        var service = new ApplicationSettingsService(
            location.SettingsPath);

        var result = service.Load();

        Assert.Equal(ApplicationSettings.Default, result.Settings);
        Assert.True(result.HasWarning);
    }

    [Fact]
    public void Load_WhenValuesAreUnsafe_NormalizesThemAndKeepsConfirmationEnabled()
    {
        using var location = new TemporarySettingsLocation();
        Directory.CreateDirectory(
            Path.GetDirectoryName(location.SettingsPath)!);
        var unsafeSettings = new ApplicationSettings(
            ApplicationTheme.Light,
            false,
            -20,
            500)
        {
            LocalDiagnosticsEnabled = true,
            IncludeHardwareSummaryInDiagnosticExport = true
        };
        File.WriteAllText(
            location.SettingsPath,
            JsonSerializer.Serialize(unsafeSettings));
        var service = new ApplicationSettingsService(
            location.SettingsPath);

        var result = service.Load();

        Assert.Equal(
            ApplicationTheme.Light,
            result.Settings.Theme);
        Assert.True(result.Settings.ConfirmBeforeCleanup);
        Assert.Equal(
            ApplicationSettings.MinimumLargeFileSizeMb,
            result.Settings.LargeFileMinimumSizeMb);
        Assert.Equal(
            ApplicationSettings.MaximumMonitorRefreshSeconds,
            result.Settings
                .SystemMonitorRefreshIntervalSeconds);
        Assert.True(result.Settings.LocalDiagnosticsEnabled);
        Assert.True(
            result.Settings
                .IncludeHardwareSummaryInDiagnosticExport);
        Assert.True(result.HasWarning);
    }

    private sealed class TemporarySettingsLocation :
        IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            $"spa-settings-tests-{Guid.NewGuid():N}");

        public string SettingsPath =>
            Path.Combine(_directory, "settings.json");

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_directory))
                {
                    Directory.Delete(_directory, true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
