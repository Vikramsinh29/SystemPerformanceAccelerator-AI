using Xunit;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Infrastructure.Services;

namespace SystemPerformanceAccelerator.Tests;

public sealed class DesktopAuthorizationHandoffTests
{
    [Fact]
    public void Parser_AcceptsExpectedFragmentBasedHandoff()
    {
        var result =
            DesktopAuthorizationHandoffParser.Parse(
                "pcspa://authorize#code=abc123_-XYZ");

        Assert.True(result.Success);
        Assert.Equal(
            "abc123_-XYZ",
            result.AuthorizationCode);
        Assert.Equal(
            "authorization_handoff_valid",
            result.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://authorize#code=abc")]
    [InlineData("pcspa://different#code=abc")]
    [InlineData("pcspa://authorize?code=abc")]
    [InlineData("pcspa://authorize/path#code=abc")]
    [InlineData("pcspa://authorize#different=abc")]
    [InlineData("pcspa://authorize#code=abc&code=def")]
    [InlineData("pcspa://authorize#code=abc%20def")]
    public void Parser_RejectsUnexpectedOrAmbiguousHandoffs(
        string? activationValue)
    {
        var result =
            DesktopAuthorizationHandoffParser.Parse(
                activationValue);

        Assert.False(result.Success);
        Assert.Null(result.AuthorizationCode);
    }

    [Fact]
    public async Task Service_ExchangesCodeAndStoresOnlyReturnedCredential()
    {
        string? exchangedCode = null;

        var expiresUtc =
            DateTimeOffset.UtcNow.AddMinutes(5);

        var store =
            new RecordingCredentialStore();

        var service =
            new DesktopAuthorizationHandoffService(
                (code, _) =>
                {
                    exchangedCode = code;

                    return Task.FromResult(
                        new InstallationAuthorizationResult(
                            true,
                            "server-issued-token",
                            expiresUtc,
                            "authorized"));
                },
                store);

        var result =
            await service.HandleAsync(
                "pcspa://authorize#code=one-time-code");

        Assert.True(result.Success);
        Assert.Equal("authorized", result.Code);

        Assert.Equal(
            "one-time-code",
            exchangedCode);

        Assert.Equal(
            "server-issued-token",
            store.SavedBearerToken);

        Assert.Equal(
            expiresUtc,
            store.SavedExpiresUtc);

        Assert.NotEqual(
            "one-time-code",
            store.SavedBearerToken);
    }

    [Fact]
    public async Task Service_DoesNotCallExchangeForInvalidHandoff()
    {
        var exchangeCalls = 0;

        var store =
            new RecordingCredentialStore();

        var service =
            new DesktopAuthorizationHandoffService(
                (_, _) =>
                {
                    exchangeCalls++;

                    return Task.FromResult(
                        new InstallationAuthorizationResult(
                            true,
                            "token",
                            DateTimeOffset.UtcNow.AddMinutes(5),
                            "authorized"));
                },
                store);

        var result =
            await service.HandleAsync(
                "https://example.test/#code=attacker");

        Assert.False(result.Success);
        Assert.Equal(0, exchangeCalls);
        Assert.Null(store.SavedBearerToken);
    }

    [Fact]
    public async Task Service_DoesNotStoreCredentialWhenExchangeFails()
    {
        var store =
            new RecordingCredentialStore();

        var service =
            new DesktopAuthorizationHandoffService(
                (_, _) =>
                    Task.FromResult(
                        new InstallationAuthorizationResult(
                            false,
                            null,
                            null,
                            "authorization_exchange_failed")),
                store);

        var result =
            await service.HandleAsync(
                "pcspa://authorize#code=single-use-code");

        Assert.False(result.Success);

        Assert.Equal(
            "authorization_exchange_failed",
            result.Code);

        Assert.Null(store.SavedBearerToken);
    }

    [Fact]
    public async Task Service_FailsClosedWhenExchangeThrows()
    {
        var store =
            new RecordingCredentialStore();

        var service =
            new DesktopAuthorizationHandoffService(
                (_, _) =>
                    throw new HttpRequestException(
                        "simulated network failure"),
                store);

        var result =
            await service.HandleAsync(
                "pcspa://authorize#code=single-use-code");

        Assert.False(result.Success);

        Assert.Equal(
            "authorization_exchange_failed",
            result.Code);

        Assert.Null(store.SavedBearerToken);
    }

    [Fact]
    public async Task Service_FailsClosedWhenCredentialStorageFails()
    {
        var store =
            new RecordingCredentialStore
            {
                ThrowOnSave = true
            };

        var service =
            new DesktopAuthorizationHandoffService(
                (_, _) =>
                    Task.FromResult(
                        new InstallationAuthorizationResult(
                            true,
                            "server-token",
                            DateTimeOffset.UtcNow.AddMinutes(5),
                            "authorized")),
                store);

        var result =
            await service.HandleAsync(
                "pcspa://authorize#code=single-use-code");

        Assert.False(result.Success);

        Assert.Equal(
            "credential_storage_failed",
            result.Code);
    }

    [Fact]
    public async Task Service_PropagatesCallerCancellation()
    {
        var store =
            new RecordingCredentialStore();

        var service =
            new DesktopAuthorizationHandoffService(
                (_, token) =>
                    Task.FromCanceled<InstallationAuthorizationResult>(
                        token),
                store);

        using var cancellation =
            new CancellationTokenSource();

        cancellation.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(
            () =>
                service.HandleAsync(
                    "pcspa://authorize#code=single-use-code",
                    cancellation.Token));
    }

    private sealed class RecordingCredentialStore :
        IDesktopCredentialStore
    {
        public string? SavedBearerToken { get; private set; }

        public DateTimeOffset? SavedExpiresUtc { get; private set; }

        public bool ThrowOnSave { get; init; }

        public Task SaveAsync(
            string bearerToken,
            DateTimeOffset expiresUtc,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnSave)
            {
                throw new IOException(
                    "simulated credential storage failure");
            }

            SavedBearerToken = bearerToken;
            SavedExpiresUtc = expiresUtc;

            return Task.CompletedTask;
        }

        public Task<DesktopCredential?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<DesktopCredential?>(null);

        public Task ClearAsync(
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}