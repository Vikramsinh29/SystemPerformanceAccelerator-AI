using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Shell;
using System.Windows.Threading;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Desktop.Commands;
using SystemPerformanceAccelerator.Desktop.Services;

namespace SystemPerformanceAccelerator.Desktop.ViewModels;

public sealed class WindowsRepairAssessmentViewModel :
    INotifyPropertyChanged
{
    private readonly IWindowsRepairAssessmentService
        _assessmentService;
    private readonly IWindowsRepairAssessmentHistoryService
        _historyService;
    private readonly IWindowsRepairAssessmentInteractionService
        _interactionService;
    private readonly IDiagnosticService _diagnosticService;
    private readonly DispatcherTimer _elapsedTimer;

    private bool _checkComponentStore = true;
    private bool _verifyProtectedSystemFiles = true;
    private bool _isBusy;
    private bool _isAssessmentRunning;
    private bool _stopAfterCurrentRequested;
    private int _progress;
    private DateTimeOffset? _assessmentStartedUtc;
    private string _status =
        "Choose one or both Microsoft read-only checks, then run an assessment.";
    private string _assessmentState = "Not assessed";
    private string _progressText = "Ready to run a read-only assessment";
    private string _currentCheckText = "Ready for assessment";
    private string _elapsedText = "Elapsed: --";
    private string _latestReference = "None";
    private WindowsRepairAssessmentResult? _latestResult;

    public WindowsRepairAssessmentViewModel(
        IWindowsRepairAssessmentService assessmentService,
        IWindowsRepairAssessmentHistoryService historyService,
        IFeatureAccessGuard featureAccessGuard,
        IDiagnosticService diagnosticService,
        IWindowsRepairAssessmentInteractionService interactionService)
    {
        _assessmentService = assessmentService ??
            throw new ArgumentNullException(nameof(assessmentService));
        _historyService = historyService ??
            throw new ArgumentNullException(nameof(historyService));
        _diagnosticService = diagnosticService ??
            throw new ArgumentNullException(nameof(diagnosticService));
        _interactionService = interactionService ??
            throw new ArgumentNullException(nameof(interactionService));
        ArgumentNullException.ThrowIfNull(featureAccessGuard);

        _elapsedTimer = new DispatcherTimer(
            DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _elapsedTimer.Tick += OnElapsedTimerTick;

        RunAssessmentCommand = new AsyncRelayCommand(
            RunAssessmentAsync,
            featureAccessGuard,
            ApplicationFeature.WindowsRepairAssessment,
            FeatureAccessRequirement.Execute,
            CanRunAssessment);
        StopAfterCurrentCheckCommand = new RelayCommand(
            RequestStopAfterCurrentCheck,
            featureAccessGuard,
            ApplicationFeature.WindowsRepairAssessment,
            FeatureAccessRequirement.Execute,
            () => IsAssessmentRunning && !StopAfterCurrentRequested);
        ExportLatestReportCommand = new AsyncRelayCommand(
            ExportLatestReportAsync,
            featureAccessGuard,
            ApplicationFeature.WindowsRepairAssessment,
            FeatureAccessRequirement.Execute,
            () => !IsBusy && HasLatestAssessment);
        OpenAssessmentFolderCommand = new RelayCommand(
            OpenAssessmentFolder,
            featureAccessGuard,
            ApplicationFeature.WindowsRepairAssessment,
            FeatureAccessRequirement.Execute,
            () => !IsBusy);
        DeleteAssessmentHistoryCommand = new RelayCommand(
            DeleteAssessmentHistory,
            featureAccessGuard,
            ApplicationFeature.WindowsRepairAssessment,
            FeatureAccessRequirement.Execute,
            () => !IsBusy && HasLatestAssessment);

        LoadLatestHistory();
    }

    public ObservableCollection<
        WindowsRepairCheckResultViewModel> Results { get; } = [];

    public AsyncRelayCommand RunAssessmentCommand { get; }

    public RelayCommand StopAfterCurrentCheckCommand { get; }

    public AsyncRelayCommand ExportLatestReportCommand { get; }

    public RelayCommand OpenAssessmentFolderCommand { get; }

    public RelayCommand DeleteAssessmentHistoryCommand { get; }

    public bool CheckComponentStore
    {
        get => _checkComponentStore;
        set
        {
            if (SetField(ref _checkComponentStore, value))
            {
                RunAssessmentCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool VerifyProtectedSystemFiles
    {
        get => _verifyProtectedSystemFiles;
        set
        {
            if (SetField(
                    ref _verifyProtectedSystemFiles,
                    value))
            {
                RunAssessmentCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value))
            {
                return;
            }

            RaiseCommandStates();
        }
    }

    public bool IsAssessmentRunning
    {
        get => _isAssessmentRunning;
        private set
        {
            if (!SetField(ref _isAssessmentRunning, value))
            {
                return;
            }

            OnPropertyChanged(nameof(TaskbarProgressState));
            StopAfterCurrentCheckCommand
                .RaiseCanExecuteChanged();
        }
    }

    public TaskbarItemProgressState TaskbarProgressState =>
        IsAssessmentRunning
            ? TaskbarItemProgressState.Indeterminate
            : TaskbarItemProgressState.None;

    public bool StopAfterCurrentRequested
    {
        get => _stopAfterCurrentRequested;
        private set
        {
            if (SetField(
                    ref _stopAfterCurrentRequested,
                    value))
            {
                StopAfterCurrentCheckCommand
                    .RaiseCanExecuteChanged();
            }
        }
    }

    public int Progress
    {
        get => _progress;
        private set => SetField(ref _progress, value);
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public string AssessmentState
    {
        get => _assessmentState;
        private set => SetField(
            ref _assessmentState,
            value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetField(ref _progressText, value);
    }

    public string CurrentCheckText
    {
        get => _currentCheckText;
        private set => SetField(
            ref _currentCheckText,
            value);
    }

    public string ElapsedText
    {
        get => _elapsedText;
        private set => SetField(
            ref _elapsedText,
            value);
    }

    public string LatestReference
    {
        get => _latestReference;
        private set => SetField(
            ref _latestReference,
            value);
    }

    public bool HasLatestAssessment =>
        _latestResult is not null;

    public string CompletedChecks =>
        Results.Count(item =>
                item.Outcome !=
                WindowsRepairAssessmentOutcome.Skipped)
            .ToString("N0");

    public string HealthyChecks =>
        Results.Count(item =>
                item.Outcome ==
                WindowsRepairAssessmentOutcome.Healthy)
            .ToString("N0");

    public string AttentionChecks =>
        Results.Count(item =>
                item.Outcome ==
                WindowsRepairAssessmentOutcome.Attention)
            .ToString("N0");

    public string InconclusiveChecks =>
        Results.Count(item =>
                item.Outcome is
                    WindowsRepairAssessmentOutcome.Inconclusive or
                    WindowsRepairAssessmentOutcome.Failed or
                    WindowsRepairAssessmentOutcome.Unsupported)
            .ToString("N0");

    public string AssessmentFolder =>
        string.IsNullOrWhiteSpace(_historyService.AssessmentRoot)
            ? "Unavailable"
            : _historyService.AssessmentRoot;

    private bool CanRunAssessment() =>
        !IsBusy &&
        (CheckComponentStore ||
         VerifyProtectedSystemFiles);

    private async Task RunAssessmentAsync()
    {
        var request = new WindowsRepairAssessmentRequest(
            CheckComponentStore,
            VerifyProtectedSystemFiles);

        if (!request.HasSelectedChecks)
        {
            Status =
                "Select at least one read-only Windows check.";
            return;
        }

        if (!_interactionService.ConfirmAssessment(request))
        {
            Status = "Assessment not started.";
            return;
        }

        IsBusy = true;
        IsAssessmentRunning = true;
        StopAfterCurrentRequested = false;
        Progress = 0;
        AssessmentState = "Assessing";
        ProgressText = "Running environment preflight...";
        CurrentCheckText = "Environment preflight";
        Status =
            "PC-SPA is checking Windows prerequisites. The application is still working; please keep this window open.";
        StartElapsedTimer();

        try
        {
            var progress =
                new Progress<WindowsRepairAssessmentProgress>(
                    ApplyProgress);

            var result = await _assessmentService.AssessAsync(
                request,
                () => StopAfterCurrentRequested,
                progress);

            await _historyService.SaveAsync(result);
            ShowResult(result);

            Status = BuildCompletionStatus(result);
        }
        catch (Exception ex)
        {
            AssessmentState = "Failed safely";
            Status =
                "The assessment failed safely. No Windows repair or system change was attempted.";

            await TryRecordUnexpectedExceptionAsync(ex);
        }
        finally
        {
            StopElapsedTimer();
            IsAssessmentRunning = false;
            IsBusy = false;
            StopAfterCurrentRequested = false;
            Progress = Math.Max(Progress, 100);
            ProgressText = AssessmentState == "Failed safely"
                ? "No repair or system change was attempted"
                : "Ready for another read-only assessment";
            CurrentCheckText = AssessmentState == "Failed safely"
                ? "Assessment failed safely"
                : "Assessment complete";
        }
    }

    private void ApplyProgress(
        WindowsRepairAssessmentProgress value)
    {
        Progress = value.Percentage;
        ProgressText = value.Message;

        if (value.CurrentCheck is null)
        {
            CurrentCheckText =
                value.CompletedChecks >= value.TotalChecks &&
                value.TotalChecks > 0
                    ? "Assessment finishing"
                    : "Environment preflight";
            return;
        }

        var checkTitle = GetCheckTitle(value.CurrentCheck.Value);
        var checkFinished =
            value.Message.Contains(
                "completed",
                StringComparison.OrdinalIgnoreCase) ||
            value.Message.Contains(
                "finished",
                StringComparison.OrdinalIgnoreCase);

        CurrentCheckText = checkFinished
            ? $"{checkTitle} completed"
            : $"{checkTitle} is running";

        if (!checkFinished &&
            !StopAfterCurrentRequested)
        {
            Status =
                $"{checkTitle} is running. Microsoft checks may take several minutes. PC-SPA is still working; please keep this window open.";
        }
    }

    private void StartElapsedTimer()
    {
        _assessmentStartedUtc = DateTimeOffset.UtcNow;
        ElapsedText = "Elapsed: 0 sec";
        _elapsedTimer.Start();
    }

    private void StopElapsedTimer()
    {
        UpdateElapsedText();
        _elapsedTimer.Stop();
        _assessmentStartedUtc = null;
    }

    private void OnElapsedTimerTick(
        object? sender,
        EventArgs e) =>
        UpdateElapsedText();

    private void UpdateElapsedText()
    {
        if (_assessmentStartedUtc is null)
        {
            return;
        }

        ElapsedText = FormatElapsed(
            DateTimeOffset.UtcNow -
            _assessmentStartedUtc.Value);
    }

    private static string FormatElapsed(
        TimeSpan elapsed)
    {
        var safeElapsed = elapsed < TimeSpan.Zero
            ? TimeSpan.Zero
            : elapsed;

        if (safeElapsed.TotalMinutes < 1)
        {
            return
                $"Elapsed: {(int)safeElapsed.TotalSeconds:N0} sec";
        }

        return
            $"Elapsed: {(int)safeElapsed.TotalMinutes:N0} min {safeElapsed.Seconds:00} sec";
    }

    private static string GetCheckTitle(
        WindowsRepairAssessmentCheck check) =>
        check switch
        {
            WindowsRepairAssessmentCheck.ComponentStoreCheckHealth =>
                "DISM CheckHealth",
            WindowsRepairAssessmentCheck.ProtectedSystemFilesVerifyOnly =>
                "SFC VerifyOnly",
            _ => "Microsoft Windows check"
        };

    private void RequestStopAfterCurrentCheck()
    {
        StopAfterCurrentRequested = true;
        Status =
            "Stop requested. The current Microsoft check is still running and will finish normally. PC-SPA remains active; please keep this window open.";
        ProgressText =
            "Waiting for the current read-only check to finish before skipping any remaining selected checks...";
    }

    private async Task ExportLatestReportAsync()
    {
        if (_latestResult is null)
        {
            Status =
                "Run an assessment before exporting a report.";
            return;
        }

        var suggestedName =
            $"PC-SPA-{_latestResult.ReferenceId}.zip";
        var destination =
            _interactionService.ChooseReportDestination(
                suggestedName);

        if (string.IsNullOrWhiteSpace(destination))
        {
            Status = "Report export cancelled.";
            return;
        }

        IsBusy = true;
        try
        {
            var exportedPath =
                await _historyService.ExportLatestAsync(
                    destination);

            if (string.IsNullOrWhiteSpace(exportedPath))
            {
                Status =
                    "No saved assessment was available to export.";
                return;
            }

            Status =
                "Assessment report exported. Open and inspect the ZIP before sharing it.";
            _interactionService.ShowMessage(
                "Assessment report exported",
                $"The sanitized report was created at:\n\n{exportedPath}\n\nInspect the ZIP before sharing it.");
        }
        catch (Exception ex)
        {
            Status =
                "The assessment report could not be exported.";
            await TryRecordUnexpectedExceptionAsync(ex);
            _interactionService.ShowMessage(
                "Report export failed",
                Status,
                isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenAssessmentFolder()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(
                    _historyService.AssessmentRoot))
            {
                Status =
                    "The assessment folder is unavailable.";
                return;
            }

            _interactionService.OpenFolder(
                _historyService.AssessmentRoot);
            Status =
                "Opened the local Windows repair assessment folder.";
        }
        catch (Exception ex)
        {
            Status =
                "The assessment folder could not be opened.";
            _ = TryRecordUnexpectedExceptionAsync(ex);
        }
    }

    private void DeleteAssessmentHistory()
    {
        if (!_interactionService.ConfirmDeleteHistory())
        {
            Status =
                "Assessment history was not deleted.";
            return;
        }

        _historyService.DeleteHistory();
        _latestResult = null;
        Results.Clear();
        LatestReference = "None";
        AssessmentState = "Not assessed";
        Progress = 0;
        ProgressText = "No saved assessment";
        CurrentCheckText = "Ready for assessment";
        ElapsedText = "Elapsed: --";
        Status =
            "Local Windows repair assessment history deleted.";
        RefreshSummary();
        RaiseCommandStates();
    }

    private void LoadLatestHistory()
    {
        try
        {
            var latest = _historyService.LoadLatest();
            if (latest is null)
            {
                return;
            }

            ShowResult(latest);
            CurrentCheckText = "Latest assessment loaded";
            ProgressText = "Saved local assessment";
            Status =
                "The latest local read-only Windows assessment is displayed.";
        }
        catch (Exception)
        {
            Status =
                "Existing assessment history could not be read. Run a fresh assessment.";
        }
    }

    private void ShowResult(
        WindowsRepairAssessmentResult result)
    {
        _latestResult = result;
        Results.Clear();

        foreach (var check in result.Checks)
        {
            Results.Add(
                new WindowsRepairCheckResultViewModel(
                    check));
        }

        LatestReference = result.ReferenceId;
        AssessmentState =
            FormatOutcome(result.OverallOutcome);
        ElapsedText = FormatElapsed(result.Duration);
        Progress = 100;
        RefreshSummary();
        RaiseCommandStates();
    }

    private static string BuildCompletionStatus(
        WindowsRepairAssessmentResult result)
    {
        if (result.StopRequested)
        {
            var skippedCount = 0;
            foreach (var check in result.Checks)
            {
                if (check.Outcome ==
                    WindowsRepairAssessmentOutcome.Skipped)
                {
                    skippedCount++;
                }
            }

            return skippedCount > 0
                ? $"Assessment {result.ReferenceId} stopped after the current check. Skipped remaining selected checks: {skippedCount:N0}."
                : $"Assessment {result.ReferenceId} completed after a stop request during the final selected check. The Microsoft check finished normally; no additional selected checks remained.";
        }

        return result.OverallOutcome switch
        {
            WindowsRepairAssessmentOutcome.Healthy =>
                $"Assessment {result.ReferenceId} completed. The selected Microsoft checks reported no classified integrity problem.",
            WindowsRepairAssessmentOutcome.Attention =>
                $"Assessment {result.ReferenceId} completed with an attention result. No repair was performed.",
            WindowsRepairAssessmentOutcome.Inconclusive =>
                $"Assessment {result.ReferenceId} completed, but at least one result was inconclusive. PC-SPA did not guess.",
            WindowsRepairAssessmentOutcome.Unsupported =>
                $"Assessment {result.ReferenceId} did not start because the environment preflight did not pass.",
            WindowsRepairAssessmentOutcome.Failed =>
                $"Assessment {result.ReferenceId} completed with a command failure. No repair was performed.",
            _ =>
                $"Assessment {result.ReferenceId} completed without changing Windows."
        };
    }

    private async Task TryRecordUnexpectedExceptionAsync(
        Exception exception)
    {
        try
        {
            await _diagnosticService.RecordExceptionAsync(
                exception,
                "Windows Repair Assessment",
                "Read-only assessment workflow",
                recovered: true,
                userDataMayHaveBeenAffected: false,
                DiagnosticSeverity.Error);
        }
        catch (Exception)
        {
        }
    }

    private void RaiseCommandStates()
    {
        RunAssessmentCommand.RaiseCanExecuteChanged();
        StopAfterCurrentCheckCommand
            .RaiseCanExecuteChanged();
        ExportLatestReportCommand
            .RaiseCanExecuteChanged();
        OpenAssessmentFolderCommand
            .RaiseCanExecuteChanged();
        DeleteAssessmentHistoryCommand
            .RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(HasLatestAssessment));
    }

    private void RefreshSummary()
    {
        OnPropertyChanged(nameof(CompletedChecks));
        OnPropertyChanged(nameof(HealthyChecks));
        OnPropertyChanged(nameof(AttentionChecks));
        OnPropertyChanged(nameof(InconclusiveChecks));
        OnPropertyChanged(nameof(HasLatestAssessment));
    }

    private static string FormatOutcome(
        WindowsRepairAssessmentOutcome outcome) =>
        outcome switch
        {
            WindowsRepairAssessmentOutcome.NotRun =>
                "Not assessed",
            WindowsRepairAssessmentOutcome.Healthy =>
                "No classified issue",
            WindowsRepairAssessmentOutcome.Attention =>
                "Attention",
            WindowsRepairAssessmentOutcome.Inconclusive =>
                "Inconclusive",
            WindowsRepairAssessmentOutcome.Unsupported =>
                "Preflight blocked",
            WindowsRepairAssessmentOutcome.Failed =>
                "Command failed",
            WindowsRepairAssessmentOutcome.Skipped =>
                "Stopped",
            _ => outcome.ToString()
        };

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(
                field,
                value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(name));
}

public sealed class WindowsRepairCheckResultViewModel
{
    public WindowsRepairCheckResultViewModel(
        WindowsRepairCheckResult model)
    {
        Model = model ??
            throw new ArgumentNullException(nameof(model));
    }

    public WindowsRepairCheckResult Model { get; }

    public string Title => Model.Title;

    public WindowsRepairAssessmentOutcome Outcome =>
        Model.Outcome;

    public string OutcomeText =>
        Model.Outcome switch
        {
            WindowsRepairAssessmentOutcome.Healthy =>
                "No classified issue",
            WindowsRepairAssessmentOutcome.Attention =>
                "Attention",
            WindowsRepairAssessmentOutcome.Inconclusive =>
                "Inconclusive",
            WindowsRepairAssessmentOutcome.Unsupported =>
                "Unsupported",
            WindowsRepairAssessmentOutcome.Failed =>
                "Failed",
            WindowsRepairAssessmentOutcome.Skipped =>
                "Skipped",
            _ => "Not run"
        };

    public string Summary => Model.Summary;

    public string DurationText =>
        Model.Duration.TotalSeconds < 1
            ? $"{Model.Duration.TotalMilliseconds:0} ms"
            : $"{Model.Duration.TotalSeconds:0.0} s";

    public string Tool =>
        string.IsNullOrWhiteSpace(Model.ExecutableName)
            ? "Not started"
            : Model.ExecutableName;

    public string Details
    {
        get
        {
            var details = string.Join(
                Environment.NewLine,
                new[]
                {
                    Model.SanitizedOutput,
                    Model.SanitizedError,
                    Model.Limitation
                }.Where(item =>
                    !string.IsNullOrWhiteSpace(item)));

            return string.IsNullOrWhiteSpace(details)
                ? Model.Summary
                : details;
        }
    }
}
