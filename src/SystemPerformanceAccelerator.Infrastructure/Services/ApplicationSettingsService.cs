using System.Text.Json;
using System.Text.Json.Serialization;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Services;

public sealed class ApplicationSettingsService : IApplicationSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ApplicationSettingsService(string? settingsPath = null)
    {
        SettingsPath = string.IsNullOrWhiteSpace(settingsPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SystemPerformanceAccelerator",
                "settings.json")
            : Path.GetFullPath(settingsPath);
    }

    public string SettingsPath { get; }

    public ApplicationSettingsLoadResult Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new ApplicationSettingsLoadResult(
                ApplicationSettings.Default,
                string.Empty);
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var stored = JsonSerializer.Deserialize<ApplicationSettings>(
                json,
                SerializerOptions);

            if (stored is null)
            {
                return DefaultWithWarning(
                    "The local settings file was empty. Safe defaults were restored.");
            }

            var normalized = Normalize(stored);
            var warning = normalized == stored
                ? string.Empty
                : "One or more invalid local settings were replaced with safe values.";

            return new ApplicationSettingsLoadResult(normalized, warning);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            JsonException or
            NotSupportedException)
        {
            return DefaultWithWarning(
                "The local settings file could not be read. Safe defaults were restored.");
        }
    }

    public void Save(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var normalized = Normalize(settings);
        var directory = Path.GetDirectoryName(SettingsPath)
            ?? throw new InvalidOperationException("The settings path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = SettingsPath + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(normalized, SerializerOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, SettingsPath, true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
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

    private static ApplicationSettings Normalize(ApplicationSettings settings)
    {
        var theme = Enum.IsDefined(settings.Theme)
            ? settings.Theme
            : ApplicationTheme.System;

        var minimumSize = Math.Clamp(
            settings.LargeFileMinimumSizeMb,
            ApplicationSettings.MinimumLargeFileSizeMb,
            ApplicationSettings.MaximumLargeFileSizeMb);

        var refreshSeconds = Math.Clamp(
            settings.SystemMonitorRefreshIntervalSeconds,
            ApplicationSettings.MinimumMonitorRefreshSeconds,
            ApplicationSettings.MaximumMonitorRefreshSeconds);

        return new ApplicationSettings(
            theme,
            true,
            minimumSize,
            refreshSeconds);
    }

    private static ApplicationSettingsLoadResult DefaultWithWarning(string warning) =>
        new(ApplicationSettings.Default, warning);
}
