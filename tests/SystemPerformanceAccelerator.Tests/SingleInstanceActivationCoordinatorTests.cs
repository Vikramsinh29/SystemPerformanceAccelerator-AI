using System.IO;
using System.Linq;
using SystemPerformanceAccelerator.Desktop.Services;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class SingleInstanceActivationCoordinatorTests
{
    [Fact]
    public void SerializeArguments_RoundTripsProtocolActivation()
    {
        const string activation =
            "pcspa://authorize#code=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

        var payload =
            SingleInstanceActivationCoordinator
                .SerializeArguments(
                    new[]
                    {
                        activation
                    });

        var result =
            SingleInstanceActivationCoordinator
                .DeserializeArguments(
                    payload);

        Assert.Single(result);
        Assert.Equal(
            activation,
            result[0]);
    }

    [Fact]
    public void SerializeArguments_PreservesOrdinaryArguments()
    {
        var arguments =
            new[]
            {
                "--ordinary",
                "C:\\Temp\\file.txt",
                "pcspa://authorize/"
            };

        var payload =
            SingleInstanceActivationCoordinator
                .SerializeArguments(
                    arguments);

        var result =
            SingleInstanceActivationCoordinator
                .DeserializeArguments(
                    payload);

        Assert.Equal(
            arguments,
            result);
    }

    [Fact]
    public void SerializeArguments_RejectsTooManyArguments()
    {
        var arguments =
            Enumerable
                .Range(
                    0,
                    33)
                .Select(
                    index => $"argument-{index}")
                .ToArray();

        Assert.Throws<ArgumentException>(
            () =>
                SingleInstanceActivationCoordinator
                    .SerializeArguments(
                        arguments));
    }

    [Fact]
    public void SerializeArguments_RejectsOversizedArgument()
    {
        var oversized =
            new string(
                'A',
                2049);

        Assert.Throws<ArgumentException>(
            () =>
                SingleInstanceActivationCoordinator
                    .SerializeArguments(
                        new[]
                        {
                            oversized
                        }));
    }

    [Fact]
    public void DeserializeArguments_RejectsInvalidJson()
    {
        Assert.Throws<InvalidDataException>(
            () =>
                SingleInstanceActivationCoordinator
                    .DeserializeArguments(
                        "{not-json"));
    }

    [Fact]
    public void DeserializeArguments_RejectsOversizedPayload()
    {
        var oversized =
            new string(
                'A',
                8193);

        Assert.Throws<InvalidDataException>(
            () =>
                SingleInstanceActivationCoordinator
                    .DeserializeArguments(
                        oversized));
    }

    [Fact]
    public async Task SecondaryInstance_ForwardsArgumentsToPrimary()
    {
        var instanceName =
            "PCSPA.Tests." +
            Guid.NewGuid().ToString("N");

        using var primary =
            new SingleInstanceActivationCoordinator(
                instanceName);

        using var secondary =
            new SingleInstanceActivationCoordinator(
                instanceName);

        Assert.True(
            primary.IsPrimaryInstance);

        Assert.False(
            secondary.IsPrimaryInstance);

        var received =
            new TaskCompletionSource<IReadOnlyList<string>>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        primary.StartListening(
            arguments =>
            {
                received.TrySetResult(
                    arguments);

                return Task.CompletedTask;
            });

        const string activation =
            "pcspa://authorize#code=BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

        var forwarded =
            await secondary
                .ForwardArgumentsToPrimaryAsync(
                    new[]
                    {
                        activation
                    });

        Assert.True(
            forwarded);

        var result =
            await received.Task.WaitAsync(
                TimeSpan.FromSeconds(5));

        Assert.Single(result);

        Assert.Equal(
            activation,
            result[0]);
    }
}