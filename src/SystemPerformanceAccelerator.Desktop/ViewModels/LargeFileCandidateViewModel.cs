using System.ComponentModel;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Desktop.ViewModels;

public sealed class LargeFileCandidateViewModel : INotifyPropertyChanged, ISelectableItem
{
    private bool _isSelected;

    public LargeFileCandidateViewModel(LargeFileCandidate model)
    {
        Model = model;
    }

    public LargeFileCandidate Model { get; }
    public string Name => Model.Name;
    public string FullPath => Model.FullPath;
    public string Location => Model.Location;
    public string SizeDisplay => Model.SizeDisplay;
    public DateTime LastModified => Model.LastModified;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
