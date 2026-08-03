using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Desktop.Commands;
using SystemPerformanceAccelerator.Desktop.Services;

namespace SystemPerformanceAccelerator.Desktop.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly IApplicationSettingsService _settingsService;
    private readonly IDiagnosticService _diagnosticService;
    private readonly IDiagnosticInteractionService _diagnosticInteractionService;
    private readonly IDiagnosticFeedbackSubmissionService
        _feedbackSubmissionService;
    private readonly IBetaAccessService? _betaAccessService;
    private readonly string _applicationVersion;
    private readonly Action<ApplicationSettings> _applySettings;
    private ApplicationTheme _selectedTheme;
    private string _largeFileMinimumSizeText;
    private string _systemMonitorRefreshIntervalText;
    private bool _localDiagnosticsEnabled;
    private bool _includeHardwareSummaryInDiagnosticExport;
    private string _feedbackAffectedArea = string.Empty;
    private string _feedbackDescription = string.Empty;
    private string _feedbackExpectedResult = string.Empty;
    private bool _includeSanitizedDiagnosticsInFeedback = true;
    private bool _feedbackConsent;
    private string? _lastSubmittedFeedbackReference;
    private string _lastReviewedDiagnosticErrorReference;
    private string _status;
    private string _betaAccessCode = string.Empty;
    private BetaAccessStatus _betaAccessStatus = BetaAccessStatus.NotActivated;
    private bool _isBetaAccessBusy;

    public SettingsViewModel(
        IApplicationSettingsService settingsService,
        ApplicationSettingsLoadResult loadResult,
        Action<ApplicationSettings> applySettings,
        IFeatureAccessGuard featureAccessGuard,
        IDiagnosticService? diagnosticService = null,
        IDiagnosticInteractionService? diagnosticInteractionService = null,
        IDiagnosticFeedbackSubmissionService? feedbackSubmissionService = null,
        IBetaAccessService? betaAccessService = null,
        string? applicationVersion = null)
    {
        _settingsService = settingsService ??
            throw new ArgumentNullException(nameof(settingsService));
        ArgumentNullException.ThrowIfNull(loadResult);
        _applySettings = applySettings ??
            throw new ArgumentNullException(nameof(applySettings));
        ArgumentNullException.ThrowIfNull(featureAccessGuard);
        _diagnosticService = diagnosticService ??
            new DisabledDiagnosticService();
        _diagnosticInteractionService =
            diagnosticInteractionService ??
            new NonInteractiveDiagnosticInteractionService();
        _feedbackSubmissionService = feedbackSubmissionService ??
            new DisabledDiagnosticFeedbackSubmissionService();
        _betaAccessService = betaAccessService;
        _applicationVersion = string.IsNullOrWhiteSpace(applicationVersion)
            ? "1.0.0"
            : applicationVersion;

        _selectedTheme = loadResult.Settings.Theme;
        _largeFileMinimumSizeText =
            loadResult.Settings.LargeFileMinimumSizeMb.ToString();
        _systemMonitorRefreshIntervalText =
            loadResult.Settings.SystemMonitorRefreshIntervalSeconds.ToString();
        _localDiagnosticsEnabled =
            loadResult.Settings.LocalDiagnosticsEnabled;
        _includeHardwareSummaryInDiagnosticExport =
            loadResult.Settings.IncludeHardwareSummaryInDiagnosticExport;
        _lastReviewedDiagnosticErrorReference =
            loadResult.Settings.LastReviewedDiagnosticErrorReference;
        _status = loadResult.HasWarning
            ? loadResult.Warning
            : loadResult.Settings.LocalDiagnosticsEnabled &&
              !_diagnosticService.IsEnabled
                ? "Settings were loaded, but local diagnostics could not be enabled. Check access to the local application-data folder."
                : "Settings are stored locally on this computer.";

        SaveCommand = new RelayCommand(
            Save,
            featureAccessGuard,
            ApplicationFeature.Settings,
            FeatureAccessRequirement.Execute);
        RestoreDefaultsCommand = new RelayCommand(
            RestoreDefaults,
            featureAccessGuard,
            ApplicationFeature.Settings,
            FeatureAccessRequirement.Execute);
        OpenDiagnosticsFolderCommand = new RelayCommand(
            OpenDiagnosticsFolder,
            featureAccessGuard,
            ApplicationFeature.Settings,
            FeatureAccessRequirement.Execute);
        ExportDiagnosticPackageCommand = new AsyncRelayCommand(
            ExportDiagnosticPackageAsync,
            featureAccessGuard,
            ApplicationFeature.Settings,
            FeatureAccessRequirement.Execute);
        CreateFeedbackPackageCommand = new AsyncRelayCommand(
            CreateFeedbackPackageAsync,
            featureAccessGuard,
            ApplicationFeature.Settings,
            FeatureAccessRequirement.Execute);
        CopySubmittedFeedbackReferenceCommand = new RelayCommand(
            CopySubmittedFeedbackReference,
            () => HasSubmittedFeedbackReference);
        DismissRecordedErrorCommand = new RelayCommand(
            DismissRecordedError,
            () => HasUnreviewedDiagnosticError);
        CopyLatestErrorReferenceCommand = new RelayCommand(
            CopyLatestErrorReference,
            featureAccessGuard,
            ApplicationFeature.Settings,
            FeatureAccessRequirement.Execute);
        DeleteDiagnosticHistoryCommand = new RelayCommand(
            DeleteDiagnosticHistory,
            featureAccessGuard,
            ApplicationFeature.Settings,
            FeatureAccessRequirement.Execute);
        ResetInstallationIdCommand = new RelayCommand(
            ResetInstallationId,
            featureAccessGuard,
            ApplicationFeature.Settings,
            FeatureAccessRequirement.Execute);
        ActivateBetaAccessCommand = new AsyncRelayCommand(
            ActivateBetaAccessAsync,
            () => !IsBetaAccessBusy &&
                  !string.IsNullOrWhiteSpace(BetaAccessCode));
        RefreshBetaAccessCommand = new AsyncRelayCommand(
            RefreshBetaAccessAsync,
            () => !IsBetaAccessBusy);
    }

    public IReadOnlyList<ApplicationTheme> ThemeOptions { get; } =
        Enum.GetValues<ApplicationTheme>();

    public RelayCommand SaveCommand { get; }

    public RelayCommand RestoreDefaultsCommand { get; }

    public RelayCommand OpenDiagnosticsFolderCommand { get; }

    public AsyncRelayCommand ExportDiagnosticPackageCommand { get; }

    public AsyncRelayCommand CreateFeedbackPackageCommand { get; }

    public RelayCommand CopySubmittedFeedbackReferenceCommand { get; }

    public RelayCommand DismissRecordedErrorCommand { get; }

    public RelayCommand CopyLatestErrorReferenceCommand { get; }

    public RelayCommand DeleteDiagnosticHistoryCommand { get; }

    public RelayCommand ResetInstallationIdCommand { get; }

    public AsyncRelayCommand ActivateBetaAccessCommand { get; }

    public AsyncRelayCommand RefreshBetaAccessCommand { get; }

    public string BetaAccessCode
    {
        get => _betaAccessCode;
        set
        {
            if (SetField(ref _betaAccessCode, value))
            {
                ActivateBetaAccessCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsBetaAccessBusy
    {
        get => _isBetaAccessBusy;
        private set
        {
            if (SetField(ref _isBetaAccessBusy, value))
            {
                ActivateBetaAccessCommand.RaiseCanExecuteChanged();
                RefreshBetaAccessCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(BetaAccessActionText));
            }
        }
    }

    public bool IsBetaAccessActive => _betaAccessStatus.IsActive;

    public string BetaAccessStateText => _betaAccessStatus.Status switch
    {
        "active" => "ACTIVE",
        "expired" => "EXPIRED",
        "service_unavailable" => "VERIFICATION UNAVAILABLE",
        "activation_rejected" => "ACTIVATION REJECTED",
        _ => "NOT ACTIVATED"
    };

    public string BetaAccessMessage =>
        _betaAccessStatus.Message ?? "Beta access status is unavailable.";

    public string BetaAccessReferenceText =>
        string.IsNullOrWhiteSpace(_betaAccessStatus.EntitlementReference)
            ? "No entitlement reference"
            : _betaAccessStatus.EntitlementReference;

    public string BetaAccessExpiryText => _betaAccessStatus.ExpiresUtc is null
        ? "No expiry date available"
        : $"Access until {_betaAccessStatus.ExpiresUtc.Value.ToLocalTime():dd MMMM yyyy, h:mm tt}";

    public string BetaAccessActionText => IsBetaAccessBusy
        ? "Please wait…"
        : IsBetaAccessActive
            ? "Verify access"
            : "Activate this PC";

    public async Task InitializeBetaAccessAsync() =>
        await RefreshBetaAccessAsync();

    public ApplicationTheme SelectedTheme
    {
        get => _selectedTheme;
        set => SetField(ref _selectedTheme, value);
    }

    public string LargeFileMinimumSizeText
    {
        get => _largeFileMinimumSizeText;
        set => SetField(ref _largeFileMinimumSizeText, value);
    }

    public string SystemMonitorRefreshIntervalText
    {
        get => _systemMonitorRefreshIntervalText;
        set => SetField(
            ref _systemMonitorRefreshIntervalText,
            value);
    }

    public bool LocalDiagnosticsEnabled
    {
        get => _localDiagnosticsEnabled;
        set
        {
            if (SetField(ref _localDiagnosticsEnabled, value))
            {
                OnPropertyChanged(nameof(DiagnosticsEnabledStatus));
            }
        }
    }

    public bool IncludeHardwareSummaryInDiagnosticExport
    {
        get => _includeHardwareSummaryInDiagnosticExport;
        set
        {
            if (SetField(
                    ref _includeHardwareSummaryInDiagnosticExport,
                    value))
            {
                OnPropertyChanged(nameof(DiagnosticExportPolicyText));
            }
        }
    }

    public bool ConfirmBeforeCleanup => true;

    public bool HasUnreviewedDiagnosticError
    {
        get
        {
            var reference = _diagnosticService.LatestErrorReference;
            return !string.IsNullOrWhiteSpace(reference) &&
                   !string.Equals(
                       reference,
                       _lastReviewedDiagnosticErrorReference,
                       StringComparison.Ordinal);
        }
    }

    public string RecordedErrorActionText =>
        HasUnreviewedDiagnosticError
            ? "A recorded error is available for review."
            : "Create a privacy-safe error report after reviewing exactly what it contains. Nothing is sent automatically.";

    public string FeedbackAffectedArea
    {
        get => _feedbackAffectedArea;
        set => SetField(ref _feedbackAffectedArea, value);
    }

    public string FeedbackDescription
    {
        get => _feedbackDescription;
        set => SetField(ref _feedbackDescription, value);
    }

    public string FeedbackExpectedResult
    {
        get => _feedbackExpectedResult;
        set => SetField(ref _feedbackExpectedResult, value);
    }

    public bool IncludeSanitizedDiagnosticsInFeedback
    {
        get => _includeSanitizedDiagnosticsInFeedback;
        set => SetField(ref _includeSanitizedDiagnosticsInFeedback, value);
    }

    public bool FeedbackConsent
    {
        get => _feedbackConsent;
        set => SetField(ref _feedbackConsent, value);
    }

    public bool HasSubmittedFeedbackReference =>
        !string.IsNullOrWhiteSpace(_lastSubmittedFeedbackReference);

    public string SubmittedFeedbackReferenceText =>
        _lastSubmittedFeedbackReference ?? string.Empty;

    public string SettingsPath => _settingsService.SettingsPath;

    public string DiagnosticsPath =>
        _diagnosticService.DiagnosticsRoot;

    public string DiagnosticsEnabledStatus =>
        LocalDiagnosticsEnabled == _diagnosticService.IsEnabled
            ? _diagnosticService.IsEnabled
                ? "Enabled • local-only • no automatic upload"
                : "Disabled • no crash records are being created"
            : "Pending save • current diagnostic state has not changed";

    public string InstallationIdText =>
        _diagnosticService.InstallationId ??
        "Not created. Enable and save local diagnostics first.";

    public string LatestErrorReferenceText =>
        _diagnosticService.LatestErrorReference ??
        "No local error reference is available.";

    public string DiagnosticExportPolicyText =>
        IncludeHardwareSummaryInDiagnosticExport
            ? "After saving, manual exports will include a basic CPU and memory summary."
            : "Manual exports exclude the optional CPU and memory summary.";

    public string DiagnosticVersionText
    {
        get
        {
            var environment = _diagnosticService.CurrentEnvironment;
            return $"Version {environment.ApplicationVersion} • build {environment.BuildIdentifier}";
        }
    }

    public string DiagnosticWindowsText =>
        _diagnosticService.CurrentEnvironment.WindowsVersion;

    public string DiagnosticRuntimeText =>
        _diagnosticService.CurrentEnvironment.RuntimeVersion;

    public string DiagnosticElevationText =>
        _diagnosticService.CurrentEnvironment.IsElevated
            ? "Administrator / elevated"
            : "Standard / not elevated";

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    private void Save()
    {
        if (!TryCreateSettings(out var settings, out var error))
        {
            Status = error;
            return;
        }

        try
        {
            _settingsService.Save(settings);
            _applySettings(settings);
            RefreshDiagnosticProperties();
            Status = settings.LocalDiagnosticsEnabled
                ? _diagnosticService.IsEnabled
                    ? "Settings saved. Privacy-safe local diagnostics are enabled; nothing is uploaded automatically."
                    : "Settings saved, but local diagnostics could not be enabled. Check access to the local application-data folder."
                : "Settings saved. Local diagnostics are disabled.";
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidOperationException)
        {
            Status =
                "Settings could not be saved. Existing settings remain active. " +
                ex.Message;
        }
    }

    private async Task ActivateBetaAccessAsync()
    {
        if (_betaAccessService is null)
        {
            ApplyBetaAccessStatus(new BetaAccessStatus(
                false, "service_unavailable", null, null, null, 0,
                "Beta access is not configured in this build."));
            return;
        }

        IsBetaAccessBusy = true;
        try
        {
            var result = await _betaAccessService.ActivateAsync(
                BetaAccessCode,
                _applicationVersion);
            ApplyBetaAccessStatus(result);
            if (result.IsActive)
            {
                BetaAccessCode = string.Empty;
            }
        }
        finally
        {
            IsBetaAccessBusy = false;
        }
    }

    private async Task RefreshBetaAccessAsync()
    {
        if (_betaAccessService is null)
        {
            return;
        }

        IsBetaAccessBusy = true;
        try
        {
            ApplyBetaAccessStatus(
                await _betaAccessService.GetStatusAsync());
        }
        finally
        {
            IsBetaAccessBusy = false;
        }
    }

    private void ApplyBetaAccessStatus(BetaAccessStatus status)
    {
        _betaAccessStatus = status;
        OnPropertyChanged(nameof(IsBetaAccessActive));
        OnPropertyChanged(nameof(BetaAccessStateText));
        OnPropertyChanged(nameof(BetaAccessMessage));
        OnPropertyChanged(nameof(BetaAccessReferenceText));
        OnPropertyChanged(nameof(BetaAccessExpiryText));
        OnPropertyChanged(nameof(BetaAccessActionText));
    }

    private void RestoreDefaults()
    {
        var defaults = ApplicationSettings.Default;
        SelectedTheme = defaults.Theme;
        LargeFileMinimumSizeText =
            defaults.LargeFileMinimumSizeMb.ToString();
        SystemMonitorRefreshIntervalText =
            defaults.SystemMonitorRefreshIntervalSeconds.ToString();
        LocalDiagnosticsEnabled =
            defaults.LocalDiagnosticsEnabled;
        IncludeHardwareSummaryInDiagnosticExport =
            defaults.IncludeHardwareSummaryInDiagnosticExport;
        _lastReviewedDiagnosticErrorReference =
            defaults.LastReviewedDiagnosticErrorReference;

        try
        {
            _settingsService.Save(defaults);
            _applySettings(defaults);
            RefreshDiagnosticProperties();
            Status =
                "Safe default settings restored and applied. Existing diagnostic history was not deleted.";
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidOperationException)
        {
            Status =
                "Defaults could not be saved. Existing settings remain active. " +
                ex.Message;
        }
    }

    private void OpenDiagnosticsFolder()
    {
        try
        {
            _diagnosticInteractionService.OpenFolder(
                _diagnosticService.DiagnosticsRoot);
            Status = "Opened the local diagnostics folder.";
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            ArgumentException or
            Win32Exception or
            System.Security.SecurityException)
        {
            Status =
                "The diagnostics folder could not be opened. " +
                ex.Message;
        }
    }

    private async Task ExportDiagnosticPackageAsync()
    {
        if (!_diagnosticService.IsEnabled)
        {
            Status =
                "Enable local diagnostics and save Settings before exporting a package.";
            return;
        }

        try
        {
            var preview = _diagnosticService.CreateExportPreview();
            if (!_diagnosticInteractionService.ConfirmExport(preview))
            {
                Status = "Diagnostic export not started.";
                return;
            }

            var suggestedName =
                $"PC-SPA-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
            var destination =
                _diagnosticInteractionService.SelectExportPath(
                    suggestedName);

            if (string.IsNullOrWhiteSpace(destination))
            {
                Status = "Diagnostic export cancelled.";
                return;
            }

            var result = await _diagnosticService.ExportAsync(
                destination);
            Status = result.Message;
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            ArgumentException or
            NotSupportedException or
            System.Security.SecurityException)
        {
            Status =
                "The diagnostic package could not be exported. " +
                ex.Message;
        }
    }

    private async Task CreateFeedbackPackageAsync()
    {
        if (!_diagnosticService.IsEnabled)
        {
            Status = "Enable local diagnostics and save Settings before creating an error report.";
            return;
        }

        if (string.IsNullOrWhiteSpace(FeedbackAffectedArea) ||
            string.IsNullOrWhiteSpace(FeedbackDescription))
        {
            Status = "Enter the affected tool or page and describe what happened.";
            return;
        }

        if (!FeedbackConsent)
        {
            Status = "Review the privacy notice and select the consent checkbox before creating the report.";
            return;
        }

        try
        {
            var preview = _diagnosticService.CreateExportPreview();
            var feedback = new DiagnosticFeedbackRequest(
                _diagnosticService.LatestErrorReference ??
                    "No recorded error reference",
                FeedbackAffectedArea.Trim(),
                FeedbackDescription.Trim(),
                FeedbackExpectedResult.Trim(),
                IncludeSanitizedDiagnosticsInFeedback);

            if (!_diagnosticInteractionService.ConfirmFeedback(
                    feedback,
                    preview))
            {
                Status = "Error report not created.";
                return;
            }

            var submission = _diagnosticService
                .CreateFeedbackSubmission(feedback);
            var result = await _feedbackSubmissionService.SubmitAsync(
                submission);

            if (result.Success)
            {
                SetSubmittedFeedbackReference(result.Reference);
                Status = result.Message;
                TryMarkLatestErrorReviewed(result.Message);
                FeedbackConsent = false;
                return;
            }

            if (!_diagnosticInteractionService
                    .ConfirmLocalFeedbackFallback(result.Message))
            {
                Status = result.Message +
                    " No information was sent or saved.";
                return;
            }

            var localResult = await CreateLocalFeedbackFallbackAsync(
                feedback);
            Status = result.Message + " " + localResult.Message;
            if (localResult.Success)
            {
                TryMarkLatestErrorReviewed(Status);
            }
            FeedbackConsent = false;
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            ArgumentException or
            NotSupportedException or
            System.Security.SecurityException)
        {
            Status = "The error report could not be created. " + ex.Message;
        }
    }

    private async Task<DiagnosticExportResult>
        CreateLocalFeedbackFallbackAsync(
            DiagnosticFeedbackRequest feedback)
    {
        var destination = _diagnosticInteractionService.SelectExportPath(
            $"PC-SPA-Error-Report-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        if (string.IsNullOrWhiteSpace(destination))
        {
            return new DiagnosticExportResult(
                false,
                string.Empty,
                0,
                "Local ZIP creation was cancelled.");
        }

        return await _diagnosticService.ExportFeedbackAsync(
            destination,
            feedback);
    }

    private void SetSubmittedFeedbackReference(string? reference)
    {
        _lastSubmittedFeedbackReference = reference;
        OnPropertyChanged(nameof(HasSubmittedFeedbackReference));
        OnPropertyChanged(nameof(SubmittedFeedbackReferenceText));
        CopySubmittedFeedbackReferenceCommand.RaiseCanExecuteChanged();
    }

    private void CopySubmittedFeedbackReference()
    {
        if (!HasSubmittedFeedbackReference)
        {
            Status = "No submitted feedback reference is available.";
            return;
        }

        try
        {
            _diagnosticInteractionService.CopyText(
                _lastSubmittedFeedbackReference!);
            Status = $"Copied feedback reference {_lastSubmittedFeedbackReference}.";
        }
        catch (ExternalException ex)
        {
            Status = "The feedback reference could not be copied. " +
                ex.Message;
        }
    }

    private void TryMarkLatestErrorReviewed(string successMessage)
    {
        try
        {
            MarkLatestErrorReviewed();
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidOperationException)
        {
            Status = successMessage +
                " The Settings action badge could not be cleared. " +
                ex.Message;
        }
    }

    private void DismissRecordedError()
    {
        if (!HasUnreviewedDiagnosticError)
        {
            Status = "No unreviewed diagnostic error is available.";
            return;
        }

        try
        {
            MarkLatestErrorReviewed();
            Status = "The recorded error was dismissed. Its local diagnostic evidence was retained.";
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidOperationException)
        {
            Status = "The recorded error could not be dismissed. " + ex.Message;
        }
    }

    private void MarkLatestErrorReviewed()
    {
        var reference = _diagnosticService.LatestErrorReference;
        if (string.IsNullOrWhiteSpace(reference))
        {
            return;
        }

        var stored = _settingsService.Load().Settings with
        {
            LastReviewedDiagnosticErrorReference = reference
        };
        _settingsService.Save(stored);
        _lastReviewedDiagnosticErrorReference = reference;
        RefreshRecordedErrorProperties();
    }

    public void RefreshDiagnosticState()
    {
        RefreshDiagnosticProperties();
        RefreshRecordedErrorProperties();
    }

    private void RefreshRecordedErrorProperties()
    {
        OnPropertyChanged(nameof(HasUnreviewedDiagnosticError));
        OnPropertyChanged(nameof(RecordedErrorActionText));
        DismissRecordedErrorCommand.RaiseCanExecuteChanged();
    }

    private void CopyLatestErrorReference()
    {
        var reference = _diagnosticService.LatestErrorReference;
        if (string.IsNullOrWhiteSpace(reference))
        {
            Status = "No local error reference is available to copy.";
            return;
        }

        try
        {
            _diagnosticInteractionService.CopyText(reference);
            Status = $"Copied error reference {reference}.";
        }
        catch (ExternalException ex)
        {
            Status =
                "The error reference could not be copied to the clipboard. " +
                ex.Message;
        }
    }

    private void DeleteDiagnosticHistory()
    {
        var preview = _diagnosticService.CreateExportPreview();
        if (!_diagnosticInteractionService.ConfirmDeleteHistory(
                preview.EventCount))
        {
            Status = "Diagnostic history was not deleted.";
            return;
        }

        try
        {
            _diagnosticService.DeleteHistory();
            RefreshDiagnosticProperties();
            Status =
                "Local diagnostic history deleted. The anonymous installation ID was retained.";
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            Status =
                "Diagnostic history could not be deleted. " +
                ex.Message;
        }
    }

    private void ResetInstallationId()
    {
        if (!_diagnosticInteractionService.ConfirmResetInstallationId())
        {
            Status = "Installation ID was not reset.";
            return;
        }

        try
        {
            _diagnosticService.ResetInstallationId();
            RefreshDiagnosticProperties();
            Status = _diagnosticService.IsEnabled
                ? "Diagnostic history deleted and a new anonymous installation ID created."
                : "Diagnostic history and the previous anonymous installation ID deleted.";
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            NotSupportedException or
            System.Security.SecurityException)
        {
            Status =
                "The anonymous installation ID could not be reset. " +
                ex.Message;
        }
    }

    private bool TryCreateSettings(
        out ApplicationSettings settings,
        out string error)
    {
        settings = ApplicationSettings.Default;

        if (!int.TryParse(
                LargeFileMinimumSizeText,
                out var minimumSize) ||
            minimumSize <
                ApplicationSettings.MinimumLargeFileSizeMb ||
            minimumSize >
                ApplicationSettings.MaximumLargeFileSizeMb)
        {
            error =
                $"Large File Finder default must be a whole number from {ApplicationSettings.MinimumLargeFileSizeMb:N0} to {ApplicationSettings.MaximumLargeFileSizeMb:N0} MB.";
            return false;
        }

        if (!int.TryParse(
                SystemMonitorRefreshIntervalText,
                out var refreshSeconds) ||
            refreshSeconds <
                ApplicationSettings.MinimumMonitorRefreshSeconds ||
            refreshSeconds >
                ApplicationSettings.MaximumMonitorRefreshSeconds)
        {
            error =
                $"System Monitor refresh interval must be a whole number from {ApplicationSettings.MinimumMonitorRefreshSeconds} to {ApplicationSettings.MaximumMonitorRefreshSeconds} seconds.";
            return false;
        }

        settings = new ApplicationSettings(
            SelectedTheme,
            true,
            minimumSize,
            refreshSeconds)
        {
            LocalDiagnosticsEnabled =
                LocalDiagnosticsEnabled,
            IncludeHardwareSummaryInDiagnosticExport =
                IncludeHardwareSummaryInDiagnosticExport,
            LastReviewedDiagnosticErrorReference =
                _lastReviewedDiagnosticErrorReference
        };
        error = string.Empty;
        return true;
    }

    private void RefreshDiagnosticProperties()
    {
        OnPropertyChanged(nameof(DiagnosticsEnabledStatus));
        OnPropertyChanged(nameof(InstallationIdText));
        OnPropertyChanged(nameof(LatestErrorReferenceText));
        OnPropertyChanged(nameof(DiagnosticExportPolicyText));
        OnPropertyChanged(nameof(DiagnosticVersionText));
        OnPropertyChanged(nameof(DiagnosticWindowsText));
        OnPropertyChanged(nameof(DiagnosticRuntimeText));
        OnPropertyChanged(nameof(DiagnosticElevationText));
        RefreshRecordedErrorProperties();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
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
