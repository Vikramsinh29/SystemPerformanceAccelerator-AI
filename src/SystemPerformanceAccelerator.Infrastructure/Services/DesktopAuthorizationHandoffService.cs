using SystemPerformanceAccelerator.Core.Interfaces;

namespace SystemPerformanceAccelerator.Infrastructure.Services;

public sealed class DesktopAuthorizationHandoffService
{
    private readonly Func<
        string,
        CancellationToken,
        Task<InstallationAuthorizationResult>>
        _exchangeAuthorizationCodeAsync;

    private readonly IDesktopCredentialStore _credentialStore;

    public DesktopAuthorizationHandoffService(
        Func<
            string,
            CancellationToken,
            Task<InstallationAuthorizationResult>>
            exchangeAuthorizationCodeAsync,
        IDesktopCredentialStore credentialStore)
    {
        _exchangeAuthorizationCodeAsync =
            exchangeAuthorizationCodeAsync ??
            throw new ArgumentNullException(
                nameof(exchangeAuthorizationCodeAsync));

        _credentialStore =
            credentialStore ??
            throw new ArgumentNullException(
                nameof(credentialStore));
    }

    public async Task<DesktopAuthorizationHandoffResult>
        HandleAsync(
            string? activationValue,
            CancellationToken cancellationToken = default)
    {
        var parsed =
            DesktopAuthorizationHandoffParser.Parse(
                activationValue);

        if (!parsed.Success ||
            parsed.AuthorizationCode is null)
        {
            return DesktopAuthorizationHandoffResult.Failed(
                parsed.Code);
        }

        InstallationAuthorizationResult exchangeResult;

        try
        {
            exchangeResult =
                await _exchangeAuthorizationCodeAsync(
                        parsed.AuthorizationCode,
                        cancellationToken)
                    .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return DesktopAuthorizationHandoffResult.Failed(
                "authorization_exchange_failed");
        }

        if (!exchangeResult.Success ||
            string.IsNullOrWhiteSpace(
                exchangeResult.BearerToken) ||
            exchangeResult.ExpiresUtc is null)
        {
            return DesktopAuthorizationHandoffResult.Failed(
                exchangeResult.Code);
        }

        try
        {
            await _credentialStore.SaveAsync(
                    exchangeResult.BearerToken,
                    exchangeResult.ExpiresUtc.Value,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return DesktopAuthorizationHandoffResult.Failed(
                "credential_storage_failed");
        }

        return DesktopAuthorizationHandoffResult.Succeeded();
    }
}

public sealed record DesktopAuthorizationHandoffResult(
    bool Success,
    string Code)
{
    public static DesktopAuthorizationHandoffResult Succeeded() =>
        new(
            true,
            "authorized");

    public static DesktopAuthorizationHandoffResult Failed(
        string code) =>
        new(
            false,
            string.IsNullOrWhiteSpace(code)
                ? "authorization_failed"
                : code);
}