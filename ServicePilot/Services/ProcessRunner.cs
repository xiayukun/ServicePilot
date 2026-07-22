using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
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
    // stdout and stderr are pumped by two concurrent tasks. The log merge/fold pipeline downstream is an
    // order-dependent state machine (it folds a line into the group started by the PREVIOUS line), so the
    // order in which lines are emitted must match the order they were read. This lock serializes emits so
    // one runner never interleaves a stderr line between two stdout lines (or vice versa) out of order.
    private readonly object _emitGate = new();

    public ProcessRunner(ScriptStep step, string workingDirectory, string? variable = null)
    {
        _step = step;
        _workingDirectory = workingDirectory;
        _variable = variable;
    }

    public event Action<LogEntry>? OutputReceived;
    public event Action<int>? Exited;

    public bool IsRunning => _process != null && !HasExitedSafe(_process);

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

        _process.Exited += (_, _) =>
        {
            try
            {
                Exited?.Invoke(_process.ExitCode);
            }
            catch
            {
                Exited?.Invoke(-1);
            }
        };

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

        _ = Task.Run(() => PumpOutputAsync(_process.StandardOutput.BaseStream, "stdout"));
        _ = Task.Run(() => PumpOutputAsync(_process.StandardError.BaseStream, "stderr"));
    }

    public async Task StopAsync()
    {
        if (_process == null || HasExitedSafe(_process))
            return;

        try
        {
            Emit(new LogEntry(LogLevel.System, "正在停止进程组。", "system", _step.Name));
            _job?.Dispose();
            _job = null;

            try
            {
                if (!HasExitedSafe(_process))
                    _process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The job may have already terminated and detached the process.
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await _process.WaitForExitAsync(cts.Token);
            }
            catch (InvalidOperationException)
            {
                // The process may have exited and detached between Kill and WaitForExitAsync.
            }

            Emit(new LogEntry(LogLevel.System, "进程已停止。", "system", _step.Name));
        }
        catch (OperationCanceledException)
        {
            Emit(new LogEntry(LogLevel.Warning, "进程组停止后仍未退出，请检查是否存在残留子进程。", "system", _step.Name));
        }
        catch (Exception ex)
        {
            Emit(new LogEntry(LogLevel.Warning, $"停止进程失败: {ex.Message}", "system", _step.Name));
        }
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
    /// Serialized emit. All process output (stdout, stderr, and runner system/warning notices) goes through
    /// here so downstream ordering is deterministic: the concurrent stdout/stderr pumps cannot deliver lines
    /// out of the order in which they were read, which is what the order-dependent log fold state machine
    /// relies on.
    /// </summary>
    private void Emit(LogEntry entry)
    {
        lock (_emitGate)
        {
            OutputReceived?.Invoke(entry);
        }
    }

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
