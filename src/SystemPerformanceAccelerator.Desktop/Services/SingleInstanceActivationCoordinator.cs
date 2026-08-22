using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace SystemPerformanceAccelerator.Desktop.Services;

public sealed class SingleInstanceActivationCoordinator :
    IDisposable
{
    private const string DefaultInstanceName =
        "PCSPA.CommercialDesktop.v1";

    private const int MaxArgumentCount = 32;
    private const int MaxArgumentLength = 2048;
    private const int MaxPayloadLength = 8192;

    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _listenerCancellation =
        new();

    private Task? _listenerTask;
    private bool _disposed;

    public SingleInstanceActivationCoordinator()
        : this(DefaultInstanceName)
    {
    }

    public SingleInstanceActivationCoordinator(
        string instanceName)
    {
        if (string.IsNullOrWhiteSpace(instanceName))
        {
            throw new ArgumentException(
                "Instance name is required.",
                nameof(instanceName));
        }

        var normalizedName =
            NormalizeInstanceName(instanceName);

        _pipeName =
            $"{normalizedName}.Activation";

        _mutex =
            new Mutex(
                initiallyOwned: true,
                name: $@"Local\{normalizedName}.Mutex",
                createdNew: out var createdNew);

        IsPrimaryInstance =
            createdNew;
    }

    public bool IsPrimaryInstance { get; }

    public void StartListening(
        Func<IReadOnlyList<string>, Task> activationHandler)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        ArgumentNullException.ThrowIfNull(
            activationHandler);

        if (!IsPrimaryInstance)
        {
            throw new InvalidOperationException(
                "Only the primary instance may listen.");
        }

        if (_listenerTask is not null)
        {
            throw new InvalidOperationException(
                "The activation listener is already running.");
        }

        _listenerTask =
            ListenLoopAsync(
                activationHandler,
                _listenerCancellation.Token);
    }

    public async Task<bool> ForwardArgumentsToPrimaryAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        if (IsPrimaryInstance)
        {
            throw new InvalidOperationException(
                "The primary instance must not forward to itself.");
        }

        var payload =
            SerializeArguments(arguments);

        for (var attempt = 0; attempt < 10; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var client =
                    new NamedPipeClientStream(
                        ".",
                        _pipeName,
                        PipeDirection.Out,
                        PipeOptions.Asynchronous |
                        PipeOptions.CurrentUserOnly);

                await client
                    .ConnectAsync(
                        250,
                        cancellationToken)
                    .ConfigureAwait(false);

                var bytes =
                    Encoding.UTF8.GetBytes(
                        payload + "\n");

                await client
                    .WriteAsync(
                        bytes,
                        cancellationToken)
                    .ConfigureAwait(false);

                await client
                    .FlushAsync(
                        cancellationToken)
                    .ConfigureAwait(false);

                return true;
            }
            catch (TimeoutException)
            {
            }
            catch (IOException)
            {
            }

            await Task
                .Delay(
                    100,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return false;
    }

    public static string SerializeArguments(
        IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(
            arguments);

        if (arguments.Count > MaxArgumentCount)
        {
            throw new ArgumentException(
                "Too many activation arguments.",
                nameof(arguments));
        }

        var normalized =
            new string[arguments.Count];

        for (var index = 0;
             index < arguments.Count;
             index++)
        {
            var argument =
                arguments[index] ??
                string.Empty;

            if (argument.Length > MaxArgumentLength)
            {
                throw new ArgumentException(
                    "An activation argument is too long.",
                    nameof(arguments));
            }

            normalized[index] =
                argument;
        }

        var payload =
            JsonSerializer.Serialize(
                normalized);

        if (payload.Length > MaxPayloadLength)
        {
            throw new ArgumentException(
                "Activation payload is too large.",
                nameof(arguments));
        }

        return payload;
    }

    public static IReadOnlyList<string> DeserializeArguments(
        string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Array.Empty<string>();
        }

        if (payload.Length > MaxPayloadLength)
        {
            throw new InvalidDataException(
                "Activation payload is too large.");
        }

        string[]? arguments;

        try
        {
            arguments =
                JsonSerializer.Deserialize<string[]>(
                    payload);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Activation payload is invalid.",
                exception);
        }

        if (arguments is null)
        {
            return Array.Empty<string>();
        }

        if (arguments.Length > MaxArgumentCount)
        {
            throw new InvalidDataException(
                "Activation payload contains too many arguments.");
        }

        foreach (var argument in arguments)
        {
            if (
                argument is null ||
                argument.Length > MaxArgumentLength)
            {
                throw new InvalidDataException(
                    "Activation payload contains an invalid argument.");
            }
        }

        return arguments;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed =
            true;

        _listenerCancellation.Cancel();

        try
        {
            _listenerTask?
                .GetAwaiter()
                .GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }

        _listenerCancellation.Dispose();

        if (IsPrimaryInstance)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }

        _mutex.Dispose();
    }

    private async Task ListenLoopAsync(
        Func<IReadOnlyList<string>, Task> activationHandler,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server =
                    new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous |
                        PipeOptions.CurrentUserOnly);

                await server
                    .WaitForConnectionAsync(
                        cancellationToken)
                    .ConfigureAwait(false);

                var payload =
                    await ReadPayloadAsync(
                        server,
                        cancellationToken)
                    .ConfigureAwait(false);

                var arguments =
                    DeserializeArguments(
                        payload);

                try
                {
                    await activationHandler(
                            arguments)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Forwarded activation must not terminate
                    // the listener or the running application.
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
            catch (InvalidDataException)
            {
                // Malformed local activation payload fails closed.
            }
        }
    }

    private static async Task<string> ReadPayloadAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var buffer =
            new byte[512];

        using var memory =
            new MemoryStream();

        while (true)
        {
            var read =
                await stream
                    .ReadAsync(
                        buffer,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            memory.Write(
                buffer,
                0,
                read);

            if (memory.Length > MaxPayloadLength + 1)
            {
                throw new InvalidDataException(
                    "Activation payload is too large.");
            }

            if (
                Array.IndexOf(
                    buffer,
                    (byte)'\n',
                    0,
                    read) >= 0)
            {
                break;
            }
        }

        var payload =
            Encoding.UTF8.GetString(
                memory.ToArray());

        var newline =
            payload.IndexOf('\n');

        if (newline >= 0)
        {
            payload =
                payload[..newline];
        }

        return payload.TrimEnd('\r');
    }

    private static string NormalizeInstanceName(
        string instanceName)
    {
        var builder =
            new StringBuilder(
                instanceName.Length);

        foreach (var character in instanceName)
        {
            builder.Append(
                char.IsLetterOrDigit(character) ||
                character is '.' or '-' or '_'
                    ? character
                    : '_');
        }

        return builder.ToString();
    }
}