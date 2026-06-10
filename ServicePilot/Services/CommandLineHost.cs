using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace ServicePilot.Services;

public static class CommandLineHost
{
    private const int AttachParentProcess = -1;
    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;

    public static async Task<int> RunAsync(string[] args)
    {
        AttachConsole(AttachParentProcess);

        var response = await TrySendToTrayAsync(args) ?? await RunOfflineAsync(args);

        WriteStandard(response.IsError ? StdErrorHandle : StdOutputHandle, response.Output + Environment.NewLine);

        return response.ExitCode;
    }

    private static async Task<CommandResponse?> TrySendToTrayAsync(string[] args)
    {
        if (ShouldSkipTrayPipe())
            return null;

        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                CommandPipeServer.PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
            await pipe.ConnectAsync(cts.Token);

            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);

            await writer.WriteLineAsync(JsonSerializer.Serialize(args));
            var responseJson = await reader.ReadLineAsync();

            return string.IsNullOrWhiteSpace(responseJson)
                ? CommandResponse.Error("托盘实例没有返回结果。")
                : JsonSerializer.Deserialize<CommandResponse>(responseJson);
        }
        catch
        {
            return null;
        }
    }

    private static bool ShouldSkipTrayPipe()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SERVICEPILOT_CONFIG_DIR")))
            return false;

        var allowPipe = Environment.GetEnvironmentVariable("SERVICEPILOT_ALLOW_TRAY_PIPE");
        return !string.Equals(allowPipe, "1", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(allowPipe, "true", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(allowPipe, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<CommandResponse> RunOfflineAsync(string[] args)
    {
        var configService = new ConfigService();
        var config = await configService.LoadAsync();
        var variableUsageStore = new PresetVariableUsageStore(configService.ConfigDirectory);
        var processor = new ServiceCommandProcessor(configService, config, variableUsageStore: variableUsageStore);
        return await processor.ExecuteAsync(args);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(
        IntPtr hFile,
        byte[] lpBuffer,
        int nNumberOfBytesToWrite,
        out int lpNumberOfBytesWritten,
        IntPtr lpOverlapped);

    private static void WriteStandard(int handleKind, string text)
    {
        var handle = GetStdHandle(handleKind);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            if (handleKind == StdErrorHandle)
                Console.Error.Write(text);
            else
                Console.Write(text);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        WriteFile(handle, bytes, bytes.Length, out _, IntPtr.Zero);
    }
}
