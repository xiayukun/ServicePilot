using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace ServicePilot.Services;

public class CommandPipeServer : IDisposable
{
    public const string PipeName = "ServicePilot.Command.v1";

    private readonly Func<string[], Task<CommandResponse>> _handler;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _serverTask;
    private bool _disposed;
    private readonly object _disposeGate = new();

    public CommandPipeServer(Func<string[], Task<CommandResponse>> handler)
    {
        _handler = handler;
    }

    public void Start()
    {
        _serverTask = Task.Run(RunAsync);
    }

    public void Dispose()
    {
        lock (_disposeGate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        try
        {
            _shutdown.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            _serverTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Shutdown is best-effort.
        }
        try
        {
            _shutdown.Dispose();
        }
        catch
        {
        }
    }

    private async Task RunAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(_shutdown.Token);

                using var reader = new StreamReader(pipe, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

                var request = await reader.ReadLineAsync();
                var args = string.IsNullOrWhiteSpace(request)
                    ? Array.Empty<string>()
                    : JsonSerializer.Deserialize<string[]>(request) ?? Array.Empty<string>();

                var response = await _handler(args);
                await writer.WriteLineAsync(JsonSerializer.Serialize(response));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(250);
            }
        }
    }
}
