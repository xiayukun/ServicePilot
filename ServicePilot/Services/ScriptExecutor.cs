using ServicePilot.Models;

namespace ServicePilot.Services;

public class ScriptExecutor : IDisposable
{
    private const int MaxVoltaRetries = 2;

    private readonly ServiceConfig _config;
    private readonly CancellationToken _cancellationToken;
    private readonly ServiceStartOptions _options;
    private ProcessRunner? _currentRunner;
    private ScriptStep? _currentStep;

    public event Action<LogEntry>? OutputReceived;
    public event Action<ProcessState>? StateChanged;
    public event Action<StepRuntimeState>? StepStateChanged;

    public ScriptExecutor(ServiceConfig config, CancellationToken cancellationToken, ServiceStartOptions? options = null)
    {
        _config = config;
        _cancellationToken = cancellationToken;
        _options = options ?? ServiceStartOptions.Empty;
    }

    public async Task RunAsync()
    {
        var allSteps = _config.ScriptSteps.OrderBy(s => s.Order).ToList();
        foreach (var skipped in allSteps.Where(s => string.IsNullOrWhiteSpace(s.Content)))
            PublishStepState(skipped, StepRunState.Skipped);
        if (!_options.OnlyStepId.HasValue)
        {
            foreach (var skipped in allSteps.Where(s => !string.IsNullOrWhiteSpace(s.Content) && !s.RunOnStart))
                PublishStepState(skipped, StepRunState.Skipped);
        }

        var steps = allSteps
            .Where(s => !string.IsNullOrWhiteSpace(s.Content))
            .ToList();

        if (_options.OnlyStepId.HasValue)
            steps = steps.Where(s => s.Id == _options.OnlyStepId.Value).ToList();
        else
            steps = steps.Where(s => s.RunOnStart).ToList();

        if (steps.Count == 0)
        {
            OutputReceived?.Invoke(new LogEntry(LogLevel.Warning, "服务没有可执行的脚本步骤。", "system"));
            StateChanged?.Invoke(ProcessState.Completed);
            return;
        }

        StateChanged?.Invoke(ProcessState.Starting);

        for (var i = 0; i < steps.Count; i++)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            var step = steps[i];
            _currentStep = step;
            PublishStepState(step, StepRunState.Running, variable: GetStepVariable(step, _options.Variable));

            var exitCode = await RunStepWithRetryAsync(step, i, steps.Count);

            if (exitCode != 0)
            {
                var runnableStep = ScriptDefinitionService.CreateRunnableStep(step, GetStepVariable(step, _options.Variable));
                OutputReceived?.Invoke(new LogEntry(
                    LogLevel.Error,
                    $"{runnableStep.Name} 失败，退出码: {exitCode}",
                    "system",
                    runnableStep.Name));
                PublishStepState(step, StepRunState.Failed, exitCode, $"退出码: {exitCode}", GetStepVariable(step, _options.Variable));
                StateChanged?.Invoke(ProcessState.StartFailed);
                return;
            }

            PublishStepState(step, StepRunState.Succeeded, exitCode, variable: GetStepVariable(step, _options.Variable));
            OutputReceived?.Invoke(new LogEntry(LogLevel.System, $"{step.Name} 完成。", "system", step.Name));
        }

        StateChanged?.Invoke(ProcessState.Completed);
    }

    public async Task<int> RunSingleStepAsync(Guid stepId, string? variable)
    {
        var step = _config.ScriptSteps.FirstOrDefault(s => s.Id == stepId);
        if (step == null)
            throw new InvalidOperationException("找不到脚本步骤。");
        if (string.IsNullOrWhiteSpace(step.Content))
            throw new InvalidOperationException("脚本步骤没有内容。");

        _currentStep = step;
        PublishStepState(step, StepRunState.Running, variable: GetStepVariable(step, variable));
        var exitCode = await RunStepWithRetryAsync(step, 0, 1, variable);
        PublishStepState(
            step,
            exitCode == 0 ? StepRunState.Succeeded : StepRunState.Failed,
            exitCode,
            exitCode == 0 ? null : $"退出码: {exitCode}",
            GetStepVariable(step, variable));

        if (exitCode == 0)
            OutputReceived?.Invoke(new LogEntry(LogLevel.System, $"{step.Name} 完成。", "system", step.Name));
        else
            OutputReceived?.Invoke(new LogEntry(LogLevel.Error, $"{step.Name} 失败，退出码: {exitCode}", "system", step.Name));

        return exitCode;
    }

    private async Task<int> RunStepWithRetryAsync(ScriptStep step, int index, int total, string? variableOverride = null)
    {
        var variable = variableOverride ?? _options.Variable;
        var effectiveVariable = GetStepVariable(step, variable);
        for (var attempt = 0; attempt <= MaxVoltaRetries; attempt++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var runnableStep = ScriptDefinitionService.CreateRunnableStep(step, effectiveVariable);
            OutputReceived?.Invoke(new LogEntry(
                LogLevel.System,
                $"--- 步骤 {index + 1}/{total}: {runnableStep.Name} ---",
                "system",
                runnableStep.Name));

            var sawVoltaError = false;
            using var runner = new ProcessRunner(runnableStep, _config.WorkingDirectory, effectiveVariable);
            _currentRunner = runner;

            runner.OutputReceived += entry =>
            {
                if (IsVoltaError(entry.Message))
                    sawVoltaError = true;
                OutputReceived?.Invoke(entry);
            };

            var exitTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            runner.Exited += code => exitTcs.TrySetResult(code);

            runner.Start();

            if (!_options.OnlyStepId.HasValue)
                StateChanged?.Invoke(ProcessState.Running);

            var exitCode = await exitTcs.Task.WaitAsync(_cancellationToken);
            _currentRunner = null;

            if (exitCode == 126 && sawVoltaError && attempt < MaxVoltaRetries)
            {
                OutputReceived?.Invoke(new LogEntry(
                    LogLevel.Warning,
                    $"检测到 Volta 126 临时启动错误，准备重试 {attempt + 1}/{MaxVoltaRetries}。",
                    "system",
                    runnableStep.Name));
                await Task.Delay(800, _cancellationToken);
                continue;
            }

            return exitCode;
        }

        return 126;
    }

    public async Task StopAsync()
    {
        if (_currentRunner != null)
            await _currentRunner.StopAsync();

        if (_currentStep != null)
            PublishStepState(_currentStep, StepRunState.Cancelled, variable: GetStepVariable(_currentStep, _options.Variable));
    }

    public void Dispose()
    {
        _currentRunner?.Dispose();
    }

    private void PublishStepState(
        ScriptStep step,
        StepRunState state,
        int? exitCode = null,
        string? error = null,
        string? variable = null)
    {
        StepStateChanged?.Invoke(new StepRuntimeState
        {
            StepId = step.Id,
            StepName = step.Name,
            State = state,
            ExitCode = exitCode,
            StartedAt = state == StepRunState.Running ? DateTime.Now : null,
            EndedAt = state is StepRunState.Succeeded or StepRunState.Failed or StepRunState.Skipped or StepRunState.Cancelled
                ? DateTime.Now
                : null,
            ActiveVariable = variable,
            Error = error
        });
    }

    private static bool IsVoltaError(string message) =>
        message.Contains("Volta error", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("volta-error", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("Could not execute command", StringComparison.OrdinalIgnoreCase);

    private static string? GetStepVariable(ScriptStep step, string? variable) =>
        step.UseVariable ? variable : null;
}
