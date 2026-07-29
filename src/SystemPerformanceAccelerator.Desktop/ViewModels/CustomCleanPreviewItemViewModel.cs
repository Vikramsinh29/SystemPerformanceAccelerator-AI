using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Desktop.ViewModels;

public sealed class CustomCleanPreviewItemViewModel
{
    public CustomCleanPreviewItemViewModel(CustomCleanPreviewItem model)
    {
        Model = model;
    }

    public CustomCleanPreviewItem Model { get; }
    public string Category => Model.CategoryName;
    public string Name => Model.Name;
    public string FullPath => Model.FullPath;
    public string Size => MainWindowViewModel.FormatBytes(Model.SizeBytes);
    public DateTime LastModified => Model.LastWriteTimeUtc.ToLocalTime();
}
