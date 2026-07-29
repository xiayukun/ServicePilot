using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Channels;
using ServicePilot.Models;

namespace ServicePilot.Services;

public class ProcessRunner : IDisposable
{
    private readonly ScriptStep _step;
    private readonly string _workingDirectory;
    private readonly string? _variable;
    private Process? _process;
    private string? _tempFile;
    private WindowsJob? _job;
    private Task<int>? _completionTask;
    private readonly Channel<OutputDispatchItem> _outputQueue = Channel.CreateUnbounded<OutputDispatchItem>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
    private readonly object _emitGate = new();
    private Exception? _outputDispatchError;
    private bool _suppressOutputDelivery;

    public ProcessRunner(ScriptStep step, string workingDirectory, string? variable = null)
    {
        _step = step;
        _workingDirectory = workingDirectory;
        _variable = variable;
        _ = Task.Run(DispatchOutputAsync);
    }

    public event Action<LogEntry>? OutputReceived;

    public bool IsRunning => _process != null && !HasExitedSafe(_process);
    public Task<int> Completion => _completionTask ??
        throw new InvalidOperationException("进程尚未启动。");

    public void Start()
    {
        if (string.IsNullOrWhiteSpace(_workingDirectory) || !Directory.Exists(_workingDirectory))
            throw new DirectoryNotFoundException($"工作目录不存在: {_workingDirectory}");

        if (string.IsNullOrWhiteSpace(_step.Content))
            throw new InvalidOperationException($"脚本动作没有内容: {_step.Name}");

        _tempFile = _step.ScriptType == ScriptType.Batch ? null : WriteTempScript(_step);

        var (fileName, arguments, argumentList, displayArguments) = GetProcessCommand(_step, _tempFile);
        var outputEncoding = Encoding.UTF8;

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments ?? string.Empty,
            WorkingDirectory = _workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = outputEncoding,
            StandardErrorEncoding = outputEncoding
        };

        if (!string.IsNullOrEmpty(_variable))
            psi.Environment[ScriptDefinitionService.VariableEnvironmentName] = _variable;

        foreach (var argument in argumentList)
            psi.ArgumentList.Add(argument);

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        Emit(new LogEntry(LogLevel.System, $"{fileName} {displayArguments}", "system", _step.Name));

        _job = WindowsJob.CreateKillOnClose();
        var started = false;
        try
        {
            started = _process.Start();
            if (!started)
                throw new InvalidOperationException("进程启动失败。");

            _job.Assign(_process);
        }
        catch (Exception ex)
        {
            if (!started)
            {
                _job.Dispose();
                _job = null;
                throw;
            }

            Emit(new LogEntry(LogLevel.Warning, $"进程加入 Windows Job 失败，将退回普通进程树停止: {ex.Message}", "system", _step.Name));
            _job.Dispose();
            _job = null;
        }

        var stdoutPump = Task.Run(() => PumpOutputAsync(_process.StandardOutput.BaseStream, "stdout"));
        var stderrPump = Task.Run(() => PumpOutputAsync(_process.StandardError.BaseStream, "stderr"));
        _completionTask = CompleteAfterExitAndDrainAsync(_process, stdoutPump, stderrPump);
    }

    public async Task StopAsync()
    {
        if (_process == null)
            return;

        Emit(new LogEntry(LogLevel.System, "正在停止进程组。", "system", _step.Name));
        _job?.Dispose();
        _job = null;
        TryKillProcessTree();

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await Completion.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            SuppressFutureOutputDelivery();
            TryKillProcessTree();
            TryCloseRedirectedStreams();

            try
            {
                await Completion.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // Stop still reports failure below. Delivery remains suppressed, so a lingering pump cannot
                // begin new subscriber callbacks after StopAsync returns.
            }

            await DrainOutputAsync().WaitAsync(TimeSpan.FromSeconds(5));
            throw new TimeoutException("进程终止后未能在限定时间内排空输出。");
        }

        Emit(new LogEntry(LogLevel.System, "进程已停止。", "system", _step.Name));
        await DrainOutputAsync();
    }

    public void Dispose()
    {
        if (_process != null)
        {
            _job?.Dispose();
            _job = null;

            if (!HasExitedSafe(_process))
            {
                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Process already exited or cannot be killed.
                }
            }

            _process.Dispose();
        }

        if (_tempFile != null && File.Exists(_tempFile))
        {
            try
            {
                File.Delete(_tempFile);
            }
            catch
            {
                // Temporary script cleanup is best-effort.
            }
        }

        _outputQueue.Writer.TryComplete();
    }

    private static bool HasExitedSafe(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return true;
        }
    }

    private async Task<int> CompleteAfterExitAndDrainAsync(
        Process process,
        Task stdoutPump,
        Task stderrPump)
    {
        await process.WaitForExitAsync().ConfigureAwait(false);
        await Task.WhenAll(stdoutPump, stderrPump).ConfigureAwait(false);
        await DrainOutputAsync().ConfigureAwait(false);

        try
        {
            return process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return -1;
        }
    }

    private static string WriteTempScript(ScriptStep step)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ServicePilot");
        Directory.CreateDirectory(tempDir);

        var ext = step.ScriptType switch
        {
            ScriptType.Batch => ".bat",
            ScriptType.PowerShell => ".ps1",
            ScriptType.Python => ".py",
            ScriptType.Node => ".js",
            _ => ".cmd"
        };

        var filePath = Path.Combine(tempDir, $"{step.Id}{ext}");

        var content = step.ScriptType == ScriptType.Batch
            ? "@echo off\r\nchcp 65001 > nul\r\n" + step.Content
            : step.Content;

        var encoding = step.ScriptType == ScriptType.PowerShell
            ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
            : new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        File.WriteAllText(filePath, content, encoding);
        return filePath;
    }

    private static (string fileName, string? arguments, string[] argumentList, string displayArguments) GetProcessCommand(
        ScriptStep step,
        string? filePath)
    {
        if (step.ScriptType == ScriptType.Batch)
        {
            var command = "chcp 65001 > nul & " + NormalizeBatchContent(step.Content);
            var arguments = $"/d /s /c \"{EscapeCommandLineQuotes(command)}\"";
            return ("cmd.exe", arguments, [], arguments);
        }

        if (filePath == null)
            throw new InvalidOperationException("脚本文件路径未创建。");

        return step.ScriptType switch
        {
            ScriptType.PowerShell => ("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{filePath}\"", [], $"-NoProfile -ExecutionPolicy Bypass -File \"{filePath}\""),
            ScriptType.Python => ("python", $"\"{filePath}\"", [], $"\"{filePath}\""),
            ScriptType.Node => ("node", $"\"{filePath}\"", [], $"\"{filePath}\""),
            _ => ("cmd.exe", null, ["/d", "/s", "/c", filePath], $"/d /s /c \"{filePath}\"")
        };
    }

    private static string NormalizeBatchContent(string content)
    {
        return string.Join(" & ", content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0));
    }

    private static string EscapeCommandLineQuotes(string value)
    {
        return value.Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private async Task PumpOutputAsync(Stream stream, string source)
    {
        var buffer = new byte[4096];
        using var line = new MemoryStream();

        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0)
                    break;

                for (var i = 0; i < read; i++)
                {
                    var current = buffer[i];
                    if (current == (byte)'\n')
                    {
                        EmitOutputLine(line, source);
                        line.SetLength(0);
                    }
                    else
                    {
                        line.WriteByte(current);
                    }
                }
            }

            if (line.Length > 0)
                EmitOutputLine(line, source);
        }
        catch (ObjectDisposedException)
        {
            // The process stream can be disposed during shutdown.
        }
        catch (InvalidOperationException)
        {
            // The process may have exited while the reader task was starting.
        }
    }

    private void EmitOutputLine(MemoryStream line, string source)
    {
        var bytes = line.ToArray();
        if (bytes.Length > 0 && bytes[^1] == (byte)'\r')
            bytes = bytes[..^1];

        var message = DecodeOutputLine(bytes);
        Emit(new LogEntry(ClassifyOutputLevel(source, message), message, source, _step.Name));
    }

    /// <summary>
    /// Serializes the program-observed enqueue order from the concurrent stdout/stderr pumps. The lock only
    /// protects queue state; subscribers are invoked by the single dispatch worker after the lock is released.
    /// This does not claim to reconstruct the streams' absolute OS generation order.
    /// </summary>
    private void Emit(LogEntry entry)
    {
        entry.StepId ??= _step.Id;
        lock (_emitGate)
        {
            if (_suppressOutputDelivery)
                return;

            if (!_outputQueue.Writer.TryWrite(new OutputDispatchItem(entry, null)))
                throw new InvalidOperationException("日志投递队列已关闭。");
        }
    }

    private async Task DispatchOutputAsync()
    {
        await foreach (var item in _outputQueue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (item.Barrier != null)
            {
                item.Barrier.TrySetResult();
                continue;
            }

            bool suppress;
            lock (_emitGate)
                suppress = _suppressOutputDelivery;

            if (suppress || item.Entry == null)
                continue;

            try
            {
                OutputReceived?.Invoke(item.Entry);
            }
            catch (Exception ex)
            {
                lock (_emitGate)
                    _outputDispatchError ??= ex;
            }
        }
    }

    private async Task DrainOutputAsync()
    {
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_emitGate)
        {
            if (!_outputQueue.Writer.TryWrite(new OutputDispatchItem(null, barrier)))
                throw new InvalidOperationException("日志投递队列已关闭。");
        }

        await barrier.Task.ConfigureAwait(false);

        Exception? dispatchError;
        lock (_emitGate)
            dispatchError = _outputDispatchError;
        if (dispatchError != null)
            throw new InvalidOperationException("日志订阅者处理失败。", dispatchError);
    }

    private void SuppressFutureOutputDelivery()
    {
        lock (_emitGate)
            _suppressOutputDelivery = true;
    }

    private void TryKillProcessTree()
    {
        if (_process == null || HasExitedSafe(_process))
            return;

        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The job may have already terminated and detached the process.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // A second forced attempt and the timeout result handle an inaccessible process.
        }
    }

    private void TryCloseRedirectedStreams()
    {
        if (_process == null)
            return;

        try { _process.StandardOutput.Close(); } catch { }
        try { _process.StandardError.Close(); } catch { }
    }

    private sealed record OutputDispatchItem(LogEntry? Entry, TaskCompletionSource? Barrier);

    private static LogLevel ClassifyOutputLevel(string source, string message)
    {
        if (!string.Equals(source, "stderr", StringComparison.OrdinalIgnoreCase))
            return LogLevel.Info;

        if (IsBenignStderr(message))
            return LogLevel.Info;

        if (LooksLikeWarning(message))
            return LogLevel.Warning;

        return LooksLikeError(message) ? LogLevel.Error : LogLevel.Info;
    }

    private static bool IsBenignStderr(string message)
    {
        return message.Contains("[webpack.Progress]", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("webpack.Progress", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("[vite]", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("building", StringComparison.OrdinalIgnoreCase) &&
               message.Contains("modules", StringComparison.OrdinalIgnoreCase) &&
               message.Contains("active", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeWarning(string message)
    {
        return message.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("warn", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeError(string message)
    {
        return message.Contains("error", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("exception", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("cannot", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("not recognized", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("could not", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("enoent", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("eaddrinuse", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("syntaxerror", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("referenceerror", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("typeerror", StringComparison.OrdinalIgnoreCase);
    }

    private static string DecodeOutputLine(byte[] bytes)
    {
        if (bytes.Length == 0)
            return string.Empty;

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            return oem.GetString(bytes);
        }
    }
}
