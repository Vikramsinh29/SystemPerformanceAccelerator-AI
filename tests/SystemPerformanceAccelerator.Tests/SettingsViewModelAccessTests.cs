using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Desktop.Services;
using SystemPerformanceAccelerator.Desktop.ViewModels;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class SettingsViewModelAccessTests
{
    [Fact]
    public async Task SignInAsync_SuccessfulSignIn_ClearsPasswordAndStoresSessionState()
    {
        var authenticationService = new StubAuthenticationService
        {
            LoginResult = new AuthLoginResult(
                true,
                "session-token",
                new AuthSession(
                    "user-1",
                    "user@example.com",
                    "User",
                    true,
                    null),
                null)
        };
        var viewModel = CreateViewModel(
            authenticationService: authenticationService,
            secureTokenStorage: new InMemorySecureTokenStorage());
        viewModel.SignInEmail = "user@example.com";
        viewModel.SignInPassword = "Password123!";

        await viewModel.SignInAsync();

        Assert.True(viewModel.IsSignedIn);
        Assert.Equal(string.Empty, viewModel.SignInPassword);
        Assert.Contains("user@example.com", viewModel.SignInMessage);
    }

    [Fact]
    public async Task SignInAsync_InvalidCredentials_ClearsPasswordAndShowsFriendlyError()
    {
        var authenticationService = new StubAuthenticationService
        {
            LoginResult = new AuthLoginResult(
                false,
                null,
                null,
                new ApiFailure(
                    ApiErrorKind.AuthenticationFailed,
                    null,
                    "invalid_credentials",
                    "Invalid credentials.",
                    false))
        };
        var secureTokenStorage = new InMemorySecureTokenStorage
        {
            SessionToken = "stale-session"
        };
        var viewModel = CreateViewModel(
            authenticationService: authenticationService,
            secureTokenStorage: secureTokenStorage);
        viewModel.SignInEmail = "user@example.com";
        viewModel.SignInPassword = "wrong";

        await viewModel.SignInAsync();

        Assert.False(viewModel.IsSignedIn);
        Assert.Equal(string.Empty, viewModel.SignInPassword);
        Assert.Equal(string.Empty, secureTokenStorage.SessionToken);
        Assert.Contains("incorrect", viewModel.SignInMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ActivateBetaAccessAsync_Success_ClearsActivationKeyAndShowsActiveStatus()
    {
        var licenseService = new StubLicenseActivationService
        {
            ActivateResult = new LicenseActivationResult(
                true,
                "license-token",
                new LicenseStatus(
                    "LIC-001",
                    "pro",
                    "active",
                    "device-1",
                    DateTimeOffset.Parse("2026-08-04T12:00:00Z"),
                    DateTimeOffset.Parse("2026-09-04T12:00:00Z"),
                    DateTimeOffset.Parse("2026-08-04T12:00:00Z")),
                null)
        };
        var viewModel = CreateViewModel(
            licenseActivationService: licenseService,
            secureTokenStorage: new InMemorySecureTokenStorage());
        viewModel.BetaAccessCode = "ACT-123";

        await viewModel.ActivateBetaAccessAsync();

        Assert.True(viewModel.IsBetaAccessActive);
        Assert.Equal(string.Empty, viewModel.BetaAccessCode);
        Assert.Equal("ACTIVE", viewModel.BetaAccessStateText);
    }

    [Fact]
    public async Task ActivateBetaAccessAsync_SendsTrimmedKeyWithoutOtherNormalization()
    {
        var licenseService = new StubLicenseActivationService
        {
            ActivateResult = new LicenseActivationResult(
                true,
                "license-token",
                new LicenseStatus(
                    "LIC-001",
                    "pro",
                    "active",
                    "device-1",
                    null,
                    null,
                    null),
                null)
        };
        var viewModel = CreateViewModel(
            licenseActivationService: licenseService,
            secureTokenStorage: new InMemorySecureTokenStorage());
        viewModel.BetaAccessCode = "  D1-Key.MixedCase-123  ";

        await viewModel.ActivateBetaAccessAsync();

        Assert.Equal("D1-Key.MixedCase-123", licenseService.LastActivationRequest?.ActivationKey);
    }

    [Fact]
    public async Task ActivateBetaAccessAsync_InvalidKey_ClearsActivationKeyAndShowsFriendlyError()
    {
        var licenseService = new StubLicenseActivationService
        {
            ActivateResult = new LicenseActivationResult(
                false,
                null,
                null,
                new ApiFailure(
                    ApiErrorKind.ValidationFailed,
                    null,
                    "invalid_activation_key",
                    "Invalid key.",
                    false))
        };
        var viewModel = CreateViewModel(
            licenseActivationService: licenseService,
            secureTokenStorage: new InMemorySecureTokenStorage());
        viewModel.BetaAccessCode = "BAD-KEY";

        await viewModel.ActivateBetaAccessAsync();

        Assert.False(viewModel.IsBetaAccessActive);
        Assert.Equal(string.Empty, viewModel.BetaAccessCode);
        Assert.Contains("invalid", viewModel.BetaAccessMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INVALID_KEY", viewModel.BetaAccessMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pending", "PENDING")]
    [InlineData("revoked", "REVOKED")]
    [InlineData("expired", "EXPIRED")]
    public async Task RefreshBetaAccessAsync_MapsNonActiveLicenseStates(
        string status,
        string expectedStateText)
    {
        var secureTokenStorage = new InMemorySecureTokenStorage
        {
            LicenseToken = "license-token"
        };
        var licenseService = new StubLicenseActivationService
        {
            ValidateResult = new LicenseValidationResult(
                true,
                new LicenseStatus(
                    "LIC-STATE",
                    "pro",
                    status,
                    "device-1",
                    DateTimeOffset.Parse("2026-08-04T12:00:00Z"),
                    DateTimeOffset.Parse("2026-09-04T12:00:00Z"),
                    DateTimeOffset.Parse("2026-08-04T12:00:00Z")),
                null)
        };
        var viewModel = CreateViewModel(
            licenseActivationService: licenseService,
            secureTokenStorage: secureTokenStorage);

        await viewModel.RefreshBetaAccessAsync();

        Assert.False(viewModel.IsBetaAccessActive);
        Assert.Equal(expectedStateText, viewModel.BetaAccessStateText);
        Assert.Equal(string.Empty, secureTokenStorage.LicenseToken);
    }

    [Fact]
    public async Task ActivateBetaAccessAsync_DeviceLimitResponse_ShowsFriendlyMessage()
    {
        var licenseService = new StubLicenseActivationService
        {
            ActivateResult = new LicenseActivationResult(
                false,
                null,
                null,
                new ApiFailure(
                    ApiErrorKind.ValidationFailed,
                    null,
                    "device_limit_reached",
                    "Device limit reached.",
                    false))
        };
        var viewModel = CreateViewModel(
            licenseActivationService: licenseService,
            secureTokenStorage: new InMemorySecureTokenStorage());
        viewModel.BetaAccessCode = "ACT-LIMIT";

        await viewModel.ActivateBetaAccessAsync();

        Assert.Contains("device limit", viewModel.BetaAccessMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ACTIVATION_LIMIT", viewModel.BetaAccessMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("INVALID_KEY", "not valid")]
    [InlineData("UNAUTHENTICATED", "Sign in again")]
    [InlineData("WRONG_USER", "different signed-in account")]
    [InlineData("PENDING", "pending")]
    [InlineData("REVOKED", "revoked")]
    [InlineData("EXPIRED", "expired")]
    [InlineData("ACTIVATION_LIMIT", "device limit")]
    [InlineData("DEVICE_ALREADY_ACTIVE", "already active")]
    public async Task ActivateBetaAccessAsync_DistinguishesBackendErrorCodes(
        string code,
        string expectedMessageFragment)
    {
        var licenseService = new StubLicenseActivationService
        {
            ActivateResult = new LicenseActivationResult(
                false,
                null,
                null,
                new ApiFailure(
                    ApiErrorKind.Conflict,
                    null,
                    code,
                    "Backend rejection.",
                    false))
        };
        var viewModel = CreateViewModel(
            licenseActivationService: licenseService,
            secureTokenStorage: new InMemorySecureTokenStorage());
        viewModel.BetaAccessCode = "ACT-123";

        await viewModel.ActivateBetaAccessAsync();

        Assert.False(viewModel.IsBetaAccessActive);
        Assert.Contains(expectedMessageFragment, viewModel.BetaAccessMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(code, viewModel.BetaAccessMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PENDING", "pending")]
    [InlineData("REVOKED", "revoked")]
    [InlineData("EXPIRED", "expired")]
    [InlineData("WRONG_USER", "different account")]
    [InlineData("ACTIVATION_LIMIT", "device limit")]
    public async Task RefreshBetaAccessAsync_DistinguishesValidationErrorCodes(
        string code,
        string expectedMessageFragment)
    {
        var secureTokenStorage = new InMemorySecureTokenStorage
        {
            LicenseToken = "license-token"
        };
        var licenseService = new StubLicenseActivationService
        {
            ValidateResult = new LicenseValidationResult(
                false,
                null,
                new ApiFailure(
                    ApiErrorKind.Conflict,
                    null,
                    code,
                    "Backend rejection.",
                    false))
        };
        var viewModel = CreateViewModel(
            licenseActivationService: licenseService,
            secureTokenStorage: secureTokenStorage);

        await viewModel.RefreshBetaAccessAsync();

        Assert.False(viewModel.IsBetaAccessActive);
        Assert.Equal(string.Empty, secureTokenStorage.LicenseToken);
        Assert.Contains(expectedMessageFragment, viewModel.BetaAccessMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(code, viewModel.BetaAccessMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeBetaAccessAsync_StartupValidationSuccess_UnlocksFeatures()
    {
        var secureTokenStorage = new InMemorySecureTokenStorage
        {
            SessionToken = "session-token",
            LicenseToken = "license-token"
        };
        var authenticationService = new StubAuthenticationService
        {
            SessionResult = new AuthSessionResult(
                true,
                new AuthSession(
                    "user-1",
                    "user@example.com",
                    "User",
                    true,
                    null),
                null)
        };
        var licenseService = new StubLicenseActivationService
        {
            ValidateResult = new LicenseValidationResult(
                true,
                new LicenseStatus(
                    "LIC-001",
                    "pro",
                    "active",
                    "device-1",
                    DateTimeOffset.Parse("2026-08-04T12:00:00Z"),
                    DateTimeOffset.Parse("2026-09-04T12:00:00Z"),
                    DateTimeOffset.Parse("2026-08-04T12:00:00Z")),
                null)
        };
        var viewModel = CreateViewModel(
            authenticationService: authenticationService,
            licenseActivationService: licenseService,
            secureTokenStorage: secureTokenStorage);

        await viewModel.InitializeBetaAccessAsync();

        Assert.True(viewModel.IsBetaAccessInitialized);
        Assert.True(viewModel.IsBetaAccessActive);
        Assert.True(viewModel.IsSignedIn);
    }

    [Fact]
    public async Task InitializeBetaAccessAsync_NetworkOutageWithStoredToken_KeepsAccessAvailable()
    {
        var secureTokenStorage = new InMemorySecureTokenStorage
        {
            LicenseToken = "license-token"
        };
        var licenseService = new StubLicenseActivationService
        {
            ValidateResult = new LicenseValidationResult(
                false,
                null,
                new ApiFailure(
                    ApiErrorKind.NetworkUnavailable,
                    null,
                    "offline",
                    "Offline.",
                    true))
        };
        var viewModel = CreateViewModel(
            licenseActivationService: licenseService,
            secureTokenStorage: secureTokenStorage);

        await viewModel.InitializeBetaAccessAsync();

        Assert.True(viewModel.IsBetaAccessActive);
        Assert.Equal("ACTIVE - OFFLINE", viewModel.BetaAccessStateText);
        Assert.Equal("license-token", secureTokenStorage.LicenseToken);
    }

    [Fact]
    public async Task DeactivateBetaAccessAsync_RemovesLocalTokenAfterSuccess()
    {
        var secureTokenStorage = new InMemorySecureTokenStorage
        {
            LicenseToken = "license-token"
        };
        var licenseService = new StubLicenseActivationService
        {
            DeactivateResult = new RemoteOperationResult(true, null)
        };
        var viewModel = CreateViewModel(
            accessInteractionService: new ConfirmingAccessInteractionService(),
            licenseActivationService: licenseService,
            secureTokenStorage: secureTokenStorage);

        await viewModel.DeactivateBetaAccessAsync();

        Assert.Equal(string.Empty, secureTokenStorage.LicenseToken);
        Assert.False(viewModel.IsBetaAccessActive);
    }

    [Fact]
    public async Task DeactivateBetaAccessAsync_RemoteFailureStillRemovesLocalToken()
    {
        var secureTokenStorage = new InMemorySecureTokenStorage
        {
            LicenseToken = "license-token"
        };
        var licenseService = new StubLicenseActivationService
        {
            DeactivateResult = new RemoteOperationResult(
                false,
                new ApiFailure(
                    ApiErrorKind.NetworkUnavailable,
                    null,
                    "offline",
                    "Offline.",
                    true))
        };
        var viewModel = CreateViewModel(
            accessInteractionService: new ConfirmingAccessInteractionService(),
            licenseActivationService: licenseService,
            secureTokenStorage: secureTokenStorage);

        await viewModel.DeactivateBetaAccessAsync();

        Assert.Equal(string.Empty, secureTokenStorage.LicenseToken);
        Assert.Contains("removed locally", viewModel.BetaAccessMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SignOutAsync_RemovesLocalSessionToken()
    {
        var secureTokenStorage = new InMemorySecureTokenStorage
        {
            SessionToken = "session-token"
        };
        var authenticationService = new StubAuthenticationService
        {
            LogoutResult = new RemoteOperationResult(true, null)
        };
        var viewModel = CreateViewModel(
            accessInteractionService: new ConfirmingAccessInteractionService(),
            authenticationService: authenticationService,
            secureTokenStorage: secureTokenStorage);
        viewModel.SignInEmail = "user@example.com";
        await viewModel.SignInAsync();

        await viewModel.SignOutAsync();

        Assert.Equal(string.Empty, secureTokenStorage.SessionToken);
        Assert.False(viewModel.IsSignedIn);
    }

    [Fact]
    public async Task RefreshBetaAccessAsync_WhenCancelled_ReportsCancellation()
    {
        var secureTokenStorage = new InMemorySecureTokenStorage
        {
            LicenseToken = "license-token"
        };
        var licenseService = new StubLicenseActivationService
        {
            ValidateAsyncHandler = async cancellationToken =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                return new LicenseValidationResult(true, null, null);
            }
        };
        var viewModel = CreateViewModel(
            licenseActivationService: licenseService,
            secureTokenStorage: secureTokenStorage);

        var refreshTask = viewModel.RefreshBetaAccessAsync();
        await Task.Delay(50);
        viewModel.CancelBetaAccessOperations();
        await refreshTask;

        Assert.Contains("cancelled", viewModel.BetaAccessMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static SettingsViewModel CreateViewModel(
        IAccessInteractionService? accessInteractionService = null,
        IAuthenticationService? authenticationService = null,
        ILicenseActivationService? licenseActivationService = null,
        ISecureTokenStorage? secureTokenStorage = null)
    {
        return new SettingsViewModel(
            new StubApplicationSettingsService(),
            new ApplicationSettingsLoadResult(
                ApplicationSettings.Default,
                string.Empty),
            _ => { },
            new AllowFeatureAccessGuard(),
            new StubDiagnosticService(),
            new StubDiagnosticInteractionService(),
            new StubDiagnosticFeedbackSubmissionService(),
            accessInteractionService,
            authenticationService,
            licenseActivationService,
            secureTokenStorage,
            "1.0.0-beta.1");
    }

    private sealed class StubApplicationSettingsService :
        IApplicationSettingsService
    {
        public string SettingsPath => "settings.json";

        public ApplicationSettingsLoadResult Load() =>
            new(ApplicationSettings.Default, string.Empty);

        public void Save(ApplicationSettings settings)
        {
        }
    }

    private sealed class StubDiagnosticService : IDiagnosticService
    {
        public bool IsEnabled => false;

        public bool IncludeHardwareSummary => false;

        public string DiagnosticsRoot => string.Empty;

        public string? InstallationId => null;

        public string? LatestErrorReference => null;

        public DiagnosticEnvironment CurrentEnvironment { get; } = new(
            "1.0.0",
            "test",
            "Windows",
            ".NET",
            false,
            null,
            null,
            null,
            null);

        public void Configure(
            bool enabled,
            bool includeHardwareSummary)
        {
        }

        public Task<string?> RecordExceptionAsync(
            Exception exception,
            string feature,
            string operationStage,
            bool recovered,
            bool userDataMayHaveBeenAffected,
            DiagnosticSeverity severity = DiagnosticSeverity.Error,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public DiagnosticExportPreview CreateExportPreview() =>
            new(0, [], false, string.Empty, string.Empty);

        public Task<DiagnosticExportResult> ExportAsync(
            string destinationZipPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new DiagnosticExportResult(
                    false,
                    destinationZipPath,
                    0,
                    string.Empty));

        public Task<DiagnosticExportResult> ExportFeedbackAsync(
            string destinationZipPath,
            DiagnosticFeedbackRequest feedback,
            CancellationToken cancellationToken = default) =>
            ExportAsync(destinationZipPath, cancellationToken);

        public DiagnosticFeedbackSubmissionRequest CreateFeedbackSubmission(
            DiagnosticFeedbackRequest feedback) =>
            throw new InvalidOperationException();

        public void DeleteHistory()
        {
        }

        public void ResetInstallationId()
        {
        }
    }

    private sealed class StubDiagnosticInteractionService :
        IDiagnosticInteractionService
    {
        public bool ConfirmExport(DiagnosticExportPreview preview) => false;

        public bool ConfirmFeedback(
            DiagnosticFeedbackRequest feedback,
            DiagnosticExportPreview preview) => false;

        public bool ConfirmLocalFeedbackFallback(
            string submissionFailure) => false;

        public string? SelectExportPath(string suggestedFileName) => null;

        public bool ConfirmDeleteHistory(int eventCount) => false;

        public bool ConfirmResetInstallationId() => false;

        public void OpenFolder(string path)
        {
        }

        public void CopyText(string value)
        {
        }
    }

    private sealed class StubDiagnosticFeedbackSubmissionService :
        IDiagnosticFeedbackSubmissionService
    {
        public Task<DiagnosticFeedbackSubmissionResult> SubmitAsync(
            DiagnosticFeedbackSubmissionRequest report,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new DiagnosticFeedbackSubmissionResult(
                    false,
                    null,
                    string.Empty));
    }

    private sealed class AllowFeatureAccessGuard : IFeatureAccessGuard
    {
        public ApplicationEdition EffectiveEdition =>
            ApplicationEdition.Free;

        public bool IsDevelopmentOverrideActive => false;

        public FeatureAccessResult GetAccess(ApplicationFeature feature) =>
            new(
                feature,
                ApplicationEdition.Free,
                FeatureAccessState.Available,
                null,
                string.Empty);

        public bool CanAccess(
            ApplicationFeature feature,
            FeatureAccessRequirement requirement) => true;
    }

    private sealed class ConfirmingAccessInteractionService :
        IAccessInteractionService
    {
        public bool ConfirmSignOut() => true;

        public bool ConfirmDeactivateLicense() => true;
    }

    private sealed class InMemorySecureTokenStorage : ISecureTokenStorage
    {
        public string SessionToken { get; set; } = string.Empty;

        public string LicenseToken { get; set; } = string.Empty;

        public Task<string?> GetSessionTokenAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(
                string.IsNullOrWhiteSpace(SessionToken)
                    ? null
                    : SessionToken);

        public Task StoreSessionTokenAsync(
            string sessionToken,
            CancellationToken cancellationToken = default)
        {
            SessionToken = sessionToken;
            return Task.CompletedTask;
        }

        public Task ClearSessionTokenAsync(
            CancellationToken cancellationToken = default)
        {
            SessionToken = string.Empty;
            return Task.CompletedTask;
        }

        public Task<string?> GetLicenseTokenAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(
                string.IsNullOrWhiteSpace(LicenseToken)
                    ? null
                    : LicenseToken);

        public Task StoreLicenseTokenAsync(
            string licenseToken,
            CancellationToken cancellationToken = default)
        {
            LicenseToken = licenseToken;
            return Task.CompletedTask;
        }

        public Task ClearLicenseTokenAsync(
            CancellationToken cancellationToken = default)
        {
            LicenseToken = string.Empty;
            return Task.CompletedTask;
        }
    }

    private sealed class StubAuthenticationService : IAuthenticationService
    {
        public AuthLoginResult LoginResult { get; set; } =
            new(false, null, null, null);

        public RemoteOperationResult LogoutResult { get; set; } =
            new(true, null);

        public AuthSessionResult SessionResult { get; set; } =
            new(false, null, null);

        public Task<AuthLoginResult> LoginAsync(
            AuthLoginRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(LoginResult);

        public Task<RemoteOperationResult> LogoutAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(LogoutResult);

        public Task<AuthSessionResult> GetSessionAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SessionResult);
    }

    private sealed class StubLicenseActivationService :
        ILicenseActivationService
    {
        public LicenseActivationResult ActivateResult { get; set; } =
            new(false, null, null, null);

        public LicenseValidationResult ValidateResult { get; set; } =
            new(false, null, null);

        public RemoteOperationResult DeactivateResult { get; set; } =
            new(true, null);

        public Func<CancellationToken, Task<LicenseValidationResult>>?
            ValidateAsyncHandler { get; set; }

        public LicenseActivationRequest? LastActivationRequest { get; private set; }

        public Task<LicenseActivationResult> ActivateAsync(
            LicenseActivationRequest request,
            CancellationToken cancellationToken = default)
        {
            LastActivationRequest = request;
            return Task.FromResult(ActivateResult);
        }

        public Task<LicenseValidationResult> ValidateAsync(
            CancellationToken cancellationToken = default) =>
            ValidateAsyncHandler is null
                ? Task.FromResult(ValidateResult)
                : ValidateAsyncHandler(cancellationToken);

        public Task<RemoteOperationResult> DeactivateAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(DeactivateResult);
    }
}
