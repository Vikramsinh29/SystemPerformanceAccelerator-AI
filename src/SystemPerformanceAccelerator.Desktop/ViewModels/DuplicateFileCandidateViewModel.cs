using System.ComponentModel;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Desktop.ViewModels;

public sealed class DuplicateFileCandidateViewModel : INotifyPropertyChanged, ISelectableItem
{
    private bool _isSelected;

    public DuplicateFileCandidateViewModel(
        DuplicateFileCandidate model,
        int groupNumber,
        int groupFileCount,
        long groupReclaimableBytes)
    {
        Model = model;
        GroupNumber = groupNumber;
        GroupDisplay = $"Group {groupNumber:N0} ({groupFileCount:N0})";
        GroupReclaimableDisplay = MainWindowViewModel.FormatBytes(groupReclaimableBytes);
    }

    public DuplicateFileCandidate Model { get; }
    public int GroupNumber { get; }
    public string GroupKey => $"{Model.SizeBytes}:{Model.Sha256Hash}";
    public string GroupDisplay { get; }
    public string GroupReclaimableDisplay { get; }
    public string Name => Model.Name;
    public string FullPath => Model.FullPath;
    public string Location => Model.Location;
    public string SizeDisplay => Model.SizeDisplay;
    public DateTime LastModified => Model.LastModified;
    public string HashDisplay => Model.Sha256Hash[..Math.Min(12, Model.Sha256Hash.Length)];

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
