using System.ComponentModel;
using System.Runtime.CompilerServices;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Desktop.ViewModels;

public sealed class CleanupCandidateViewModel : INotifyPropertyChanged, ISelectableItem
{
    private bool _isSelected;

    public CleanupCandidateViewModel(CleanupCandidate model)
    {
        Model = model;
        _isSelected = model.IsSelected;
    }

    public CleanupCandidate Model { get; }
    public string Name => Model.Name;
    public string FullPath => Model.FullPath;
    public string Size => MainWindowViewModel.FormatBytes(Model.SizeBytes);
    public DateTime LastModified => Model.LastWriteTimeUtc.ToLocalTime();

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
