using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Desktop.ViewModels;

public sealed class DuplicateFileCandidateViewModel
{
    public DuplicateFileCandidateViewModel(
        DuplicateFileCandidate model,
        int groupNumber,
        int groupFileCount,
        long groupReclaimableBytes)
    {
        Model = model;
        GroupDisplay = $"Group {groupNumber:N0} ({groupFileCount:N0})";
        GroupReclaimableDisplay = MainWindowViewModel.FormatBytes(groupReclaimableBytes);
    }

    public DuplicateFileCandidate Model { get; }
    public string GroupDisplay { get; }
    public string GroupReclaimableDisplay { get; }
    public string Name => Model.Name;
    public string FullPath => Model.FullPath;
    public string Location => Model.Location;
    public string SizeDisplay => Model.SizeDisplay;
    public DateTime LastModified => Model.LastModified;
    public string HashDisplay => Model.Sha256Hash[..Math.Min(12, Model.Sha256Hash.Length)];
}
