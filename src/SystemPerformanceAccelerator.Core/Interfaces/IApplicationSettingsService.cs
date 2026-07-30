using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface IApplicationSettingsService
{
    string SettingsPath { get; }

    ApplicationSettingsLoadResult Load();

    void Save(ApplicationSettings settings);
}
