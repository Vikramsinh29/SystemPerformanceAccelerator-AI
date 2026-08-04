using System.Text;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Desktop.Services;
using SystemPerformanceAccelerator.Desktop.ViewModels;
using SystemPerformanceAccelerator.Infrastructure.Services;
using Xunit;
using Xunit.Abstractions;

namespace SystemPerformanceAccelerator.Tests;

public sealed class LiveDesktopLicensingIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public LiveDesktopLicensingIntegrationTests(
        ITestOutputHelper output)
    {
        _output = output;
    }

    [LiveIntegrationFact]
    [Trait("Category", "LiveIntegration")]
    public async Task LiveDesktopFlow_ValidatesSignInActivationRestartOfflineRecoveryAndCleanup()
    {
        var configuration = LiveConfiguration.Load();
        using var location = new TemporaryLocation();
        var secureTokenStorage = new FileSecureTokenStorage(
            location.TokenPath);

        var onlineServices = CreateServices(
            configuration.BaseUrl,
            secureTokenStorage,
            location.DeviceIdPath);
        var onlineViewModel = CreateViewModel(
            secureTokenStorage,
            onlineServices.AuthenticationService,
            onlineServices.LicenseActivationService);

        onlineViewModel.SignInEmail = configuration.Email;
        onlineViewModel.SignInPassword = configuration.Password;
        await onlineViewModel.SignInAsync();

        Assert.True(onlineViewModel.IsSignedIn);
        Assert.Equal(string.Empty, onlineViewModel.SignInPassword);
        Assert.True(File.Exists(location.TokenPath));

        var protectedContent = File.ReadAllBytes(location.TokenPath);
        var protectedText = Encoding.UTF8.GetString(protectedContent);
        Assert.DoesNotContain(configuration.Password, protectedText, StringComparison.Ordinal);
        Assert.DoesNotContain(configuration.Email, protectedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(configuration.ActivationKey, protectedText, StringComparison.Ordinal);
        _output.WriteLine("Live sign-in succeeded. Session state persisted with redacted local storage checks.");

        onlineViewModel.BetaAccessCode = configuration.ActivationKey;
        await onlineViewModel.ActivateBetaAccessAsync();

        Assert.True(onlineViewModel.IsBetaAccessActive);
        Assert.Equal(string.Empty, onlineViewModel.BetaAccessCode);
        Assert.True(File.Exists(location.TokenPath));
        _output.WriteLine(
            $"Live activation succeeded. Status={onlineViewModel.BetaAccessStateText}, Expiry={onlineViewModel.BetaAccessExpiryText}.");

        onlineViewModel.BetaAccessCode =
            configuration.ActivationKey + "-INVALID";
        await onlineViewModel.ActivateBetaAccessAsync();
        Assert.False(onlineViewModel.IsBetaAccessBusy);
        Assert.Equal(string.Empty, onlineViewModel.BetaAccessCode);
        Assert.Contains(
            "invalid",
            onlineViewModel.BetaAccessMessage,
            StringComparison.OrdinalIgnoreCase);
        _output.WriteLine("Invalid-key path returned a mapped licensing error.");

        var restartedViewModel = CreateViewModel(
            secureTokenStorage,
            onlineServices.AuthenticationService,
            onlineServices.LicenseActivationService);
        await restartedViewModel.InitializeBetaAccessAsync();

        Assert.True(restartedViewModel.IsBetaAccessInitialized);
        Assert.True(restartedViewModel.IsBetaAccessActive);
        _output.WriteLine("Restart simulation succeeded. Startup validation restored access from saved secure token.");

        var offlineServices = CreateServices(
            new Uri("https://127.0.0.1:1/"),
            secureTokenStorage,
            location.DeviceIdPath);
        var offlineViewModel = CreateViewModel(
            secureTokenStorage,
            offlineServices.AuthenticationService,
            offlineServices.LicenseActivationService);
        await offlineViewModel.InitializeBetaAccessAsync();

        Assert.True(offlineViewModel.IsBetaAccessActive);
        Assert.Equal("ACTIVE - OFFLINE", offlineViewModel.BetaAccessStateText);
        _output.WriteLine("Offline recovery path succeeded. Temporary network failure did not permanently lock the tester out.");

        var recoveredViewModel = CreateViewModel(
            secureTokenStorage,
            onlineServices.AuthenticationService,
            onlineServices.LicenseActivationService);
        await recoveredViewModel.InitializeBetaAccessAsync();

        Assert.True(recoveredViewModel.IsBetaAccessActive);
        _output.WriteLine("Online recovery after offline validation succeeded.");

        await recoveredViewModel.DeactivateBetaAccessAsync();
        Assert.False(recoveredViewModel.IsBetaAccessActive);
        Assert.Null(await secureTokenStorage.GetLicenseTokenAsync());
        _output.WriteLine("Live deactivation succeeded and cleared the local license token.");

        await recoveredViewModel.SignOutAsync();
        Assert.False(recoveredViewModel.IsSignedIn);
        Assert.Null(await secureTokenStorage.GetSessionTokenAsync());
        _output.WriteLine("Live sign-out succeeded and cleared the local session token.");
    }

    private static SettingsViewModel CreateViewModel(
        ISecureTokenStorage secureTokenStorage,
        IAuthenticationService authenticationService,
        ILicenseActivationService licenseActivationService)
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
            new ConfirmingAccessInteractionService(),
            authenticationService,
            licenseActivationService,
            secureTokenStorage,
            "1.0.0-beta.1");
    }

    private static LiveServices CreateServices(
        Uri baseUrl,
        ISecureTokenStorage secureTokenStorage,
        string deviceIdPath)
    {
        var httpClient = new HttpClient
        {
            BaseAddress = baseUrl,
            Timeout = Timeout.InfiniteTimeSpan
        };
        var apiClient = new DesktopApiClient(
            httpClient,
            TimeSpan.FromSeconds(20));
        var authenticationService = new AuthenticationService(
            apiClient,
            secureTokenStorage);
        var licenseActivationService = new LicenseActivationService(
            apiClient,
            secureTokenStorage,
            new WindowsDeviceIdentityProvider(deviceIdPath));
        return new LiveServices(
            authenticationService,
            licenseActivationService);
    }

    private sealed record LiveServices(
        IAuthenticationService AuthenticationService,
        ILicenseActivationService LicenseActivationService);

    private sealed class LiveConfiguration
    {
        public required string Email { get; init; }

        public required string Password { get; init; }

        public required string ActivationKey { get; init; }

        public required Uri BaseUrl { get; init; }

        public static LiveConfiguration Load() => new()
        {
            Email = Environment.GetEnvironmentVariable("PCSPA_TEST_EMAIL")!,
            Password = Environment.GetEnvironmentVariable("PCSPA_TEST_PASSWORD")!,
            ActivationKey = Environment.GetEnvironmentVariable("PCSPA_TEST_ACTIVATION_KEY")!,
            BaseUrl = new Uri(
                Environment.GetEnvironmentVariable("PCSPA_API_BASE_URL")!,
                UriKind.Absolute)
        };
    }

    private sealed class TemporaryLocation : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(),
            $"pc-spa-live-licensing-tests-{Guid.NewGuid():N}");

        public string TokenPath =>
            Path.Combine(Root, "tokens.dat");

        public string DeviceIdPath =>
            Path.Combine(Root, "device-id.txt");

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
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
}
