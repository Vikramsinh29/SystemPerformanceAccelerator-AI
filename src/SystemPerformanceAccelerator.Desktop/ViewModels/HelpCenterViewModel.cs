using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;
using SystemPerformanceAccelerator.Desktop.Commands;

namespace SystemPerformanceAccelerator.Desktop.ViewModels;

public sealed class HelpCenterViewModel : INotifyPropertyChanged
{
    private string _searchText = string.Empty;

    public HelpCenterViewModel(
        ICommand openCleaner,
        ICommand openHealthCheck,
        ICommand openCustomClean,
        ICommand openAutoCleanSchedule,
        ICommand openLargeFileFinder,
        ICommand openDuplicateFinder,
        ICommand openStartupManager,
        ICommand openWindowsRepair,
        ICommand openSystemMonitor)
    {
        Guides =
        [
            new(
                "Storage is getting full",
                "Cleaner",
                "Use Cleaner for reviewed temporary-file cleanup.",
                "Use it when Windows temporary storage is growing or you want a safe first cleanup.",
                "Do not expect it to remove personal documents or automatically select everything.",
                "Scan, review every candidate, select only what you recognize, then confirm cleanup.",
                "PC-SPA never deletes or recycles a file without explicit confirmation.",
                openCleaner),
            new(
                "Large files are consuming space",
                "Large File Finder",
                "Locate unusually large files and decide which ones can be recycled.",
                "Use it after Cleaner when storage is still low and you need to find personal large files.",
                "Do not recycle files you do not recognize or files required by another application.",
                "Choose a location, set a sensible minimum size, scan, review full paths, and select files manually.",
                "Selected files go to the Windows Recycle Bin so they can normally be restored.",
                openLargeFileFinder),
            new(
                "Duplicate files may be wasting space",
                "Duplicate Finder",
                "Find files whose content is confirmed to be identical.",
                "Use it for photo, download, or document folders that may contain repeated copies.",
                "Do not remove every file in a duplicate group; keep the copy stored in the correct location.",
                "Select a folder, scan, compare paths and dates, keep one copy, then recycle selected extras.",
                "PC-SPA protects at least one copy in each confirmed duplicate group.",
                openDuplicateFinder),
            new(
                "Windows starts slowly",
                "Startup Manager",
                "Review applications configured to start with Windows.",
                "Use it when many non-essential applications open automatically after sign-in.",
                "Do not disable security software, hardware utilities, or entries you do not understand.",
                "Review publisher and path information, disable one non-essential entry at a time, then restart later to evaluate.",
                "Disabling a startup entry does not uninstall or delete the application.",
                openStartupManager),
            new(
                "I want an overall PC check",
                "Health Check",
                "Review important system conditions and receive safe next-step recommendations.",
                "Use it when you are unsure which PC-SPA tool matches the problem.",
                "It is an assessment, not a guarantee that every hardware or Windows problem will be detected.",
                "Run the check, review each result, and use only the recommended tool links that match your concern.",
                "Health Check does not repair Windows or remove files automatically.",
                openHealthCheck),
            new(
                "Windows files may be damaged",
                "Windows Repair",
                "Assess Windows component-store and protected-file integrity before considering guided repair.",
                "Use it after repeated Windows errors, failed updates, or when Health Check recommends integrity assessment.",
                "Do not start repair during a power interruption, active Windows Update, or when you cannot keep the PC available.",
                "Run the read-only assessment, select Review and repair, verify readiness, review every step, then start guided repair only if allowed.",
                "Assessment is read-only. Repair requires separate confirmation, uses Microsoft tools, and never restarts Windows automatically.",
                openWindowsRepair),
            new(
                "I need controlled cleanup choices",
                "Custom Clean",
                "Choose specific supported cleanup categories instead of using the general Cleaner list.",
                "Use it when you understand the categories and want a narrower, repeatable cleanup review.",
                "Do not select a category whose contents or effect you do not understand.",
                "Choose categories, preview the candidates, review the paths, and confirm only the items you intend to remove.",
                "The same mandatory review and confirmation protections apply as in Cleaner.",
                openCustomClean),
            new(
                "I want cleanup reminders",
                "Auto Clean Schedule",
                "Plan local cleanup reminders and start reviewed cleanup manually.",
                "Use it when you want a regular reminder to review accumulated temporary files.",
                "It does not silently run destructive cleanup in the background.",
                "Choose a schedule, save it, and use the reminder to open PC-SPA and review cleanup candidates.",
                "Every cleanup still requires review and confirmation.",
                openAutoCleanSchedule),
            new(
                "I want to watch CPU and memory",
                "System Monitor",
                "View current total processor and physical-memory usage.",
                "Use it while investigating slow response, high load, or the effect of opening and closing applications.",
                "It does not identify every root cause or change process priorities.",
                "Open the monitor, observe usage over time, and compare readings before and after closing known applications.",
                "System Monitor is read-only and does not optimize or terminate processes.",
                openSystemMonitor)
        ];

        FilteredGuides = CollectionViewSource.GetDefaultView(Guides);
        FilteredGuides.Filter = MatchesSearch;
        ClearSearchCommand = new RelayCommand(
            () => SearchText = string.Empty,
            () => !string.IsNullOrEmpty(SearchText));
    }

    public ObservableCollection<ToolHelpGuideViewModel> Guides { get; }

    public ICollectionView FilteredGuides { get; }

    public RelayCommand ClearSearchCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            value ??= string.Empty;
            if (!SetField(ref _searchText, value))
            {
                return;
            }

            FilteredGuides.Refresh();
            ClearSearchCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(VisibleGuideCountText));
            OnPropertyChanged(nameof(HasVisibleGuides));
            OnPropertyChanged(nameof(HasNoVisibleGuides));
        }
    }

    public bool HasVisibleGuides => FilteredGuides.Cast<object>().Any();

    public bool HasNoVisibleGuides => !HasVisibleGuides;

    public string VisibleGuideCountText
    {
        get
        {
            var count = FilteredGuides.Cast<object>().Count();
            return count == 1 ? "1 guide" : $"{count} guides";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool MatchesSearch(object item)
    {
        if (item is not ToolHelpGuideViewModel guide)
        {
            return false;
        }

        var query = SearchText.Trim();
        return query.Length == 0 ||
               guide.SearchableText.Contains(
                   query,
                   StringComparison.OrdinalIgnoreCase);
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}

public sealed record ToolHelpGuideViewModel(
    string Problem,
    string ToolName,
    string Summary,
    string WhenToUse,
    string WhenNotToUse,
    string Steps,
    string SafetyNote,
    ICommand OpenToolCommand)
{
    public string SearchableText => string.Join(
        ' ',
        Problem,
        ToolName,
        Summary,
        WhenToUse,
        WhenNotToUse,
        Steps,
        SafetyNote);
}
