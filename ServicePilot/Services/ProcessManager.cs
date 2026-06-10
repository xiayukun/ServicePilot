using System.Collections.ObjectModel;
using System.Windows;
using ServicePilot.Models;

namespace ServicePilot.Services;

public class ProcessManager : IDisposable
{
    private readonly Dictionary<Guid, ScriptExecutor> _executors = new();
    private readonly Dictionary<Guid, CancellationTokenSource> _cancellationTokens = new();
    private readonly Dictionary<(Guid ServiceId, Guid StepId), ScriptExecutor> _stepExecutors = new();
    private readonly Dictionary<(Guid ServiceId, Guid StepId), CancellationTokenSource> _stepCancellationTokens = new();
    private readonly Dictionary<Guid, ServiceRuntimeState> _runtimeStates = new();
    private readonly object _gate = new();

    public ObservableCollection<ServiceRuntimeState> Services { get; } = new();

    public event Action<Guid, LogEntry>? ServiceOutput;
    public event Action<Guid, ProcessState>? ServiceStateChanged;
    public event Action<Guid, StepRuntimeState>? StepStateChanged;

    public void LoadConfigs(List<ServiceConfig> configs)
    {
        foreach (var config in configs.OrderBy(c => c.SortOrder))
            AddService(config);
    }

    public bool AddService(ServiceConfig config)
    {
        lock (_gate)
        {
            if (_runtimeStates.ContainsKey(config.Id))
                return false;

            var state = new ServiceRuntimeState { Config = config };
            EnsureStepStates(state);
            _runtimeStates[config.Id] = state;
            RunOnUiThread(() => Services.Add(state));
            return true;
        }
    }

    public async Task<bool> RemoveServiceAsync(Guid serviceId)
    {
        ServiceRuntimeState? state;
        lock (_gate)
        {
            if (!_runtimeStates.TryGetValue(serviceId, out state))
                return false;
        }

        await StopServiceAsync(serviceId);

        lock (_gate)
        {
            _runtimeStates.Remove(serviceId);
        }

        RunOnUiThread(() => Services.Remove(state));
        return true;
    }

    public bool UpdateService(ServiceConfig config)
    {
        lock (_gate)
        {
            if (!_runtimeStates.TryGetValue(config.Id, out var state))
                return false;

            state.Config = config;
            EnsureStepStates(state);
            return true;
        }
    }

    public ServiceRuntimeState? FindService(string selector)
    {
        if (Guid.TryParse(selector, out var id))
        {
            lock (_gate)
            {
                return _runtimeStates.GetValueOrDefault(id);
            }
        }

        lock (_gate)
        {
            return _runtimeStates.Values
                .FirstOrDefault(s => string.Equals(s.Config.Name, selector, StringComparison.OrdinalIgnoreCase));
        }
    }

    public IReadOnlyList<ServiceRuntimeState> Snapshot()
    {
        lock (_gate)
        {
            return _runtimeStates.Values
                .OrderBy(s => s.Config.SortOrder)
                .ThenBy(s => s.Config.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public bool StartService(Guid serviceId, ServiceStartOptions? options = null)
    {
        var startOptions = options ?? ServiceStartOptions.Empty;
        ServiceRuntimeState? state;
        lock (_gate)
        {
            if (!_runtimeStates.TryGetValue(serviceId, out state))
                return false;

            if (state.State == ProcessState.Running || state.State == ProcessState.Starting)
                return true;

            ResetStepStatesForRun(state, startOptions.Variable);
        }

        var runtimeState = state ?? throw new InvalidOperationException("Service state was not loaded.");
        RunOnUiThread(() =>
        {
            runtimeState.State = ProcessState.Starting;
            runtimeState.LastError = null;
            runtimeState.ActiveVariable = startOptions.Variable;
            ServiceStateChanged?.Invoke(serviceId, ProcessState.Starting);
        });

        var cts = new CancellationTokenSource();
        runtimeState.ActiveVariable = startOptions.Variable;
        var executor = new ScriptExecutor(runtimeState.Config, cts.Token, startOptions);

        lock (_gate)
        {
            _cancellationTokens[serviceId] = cts;
            _executors[serviceId] = executor;
        }

        WireExecutor(serviceId, runtimeState, executor, updateServiceState: true);

        _ = Task.Run(async () =>
        {
            try
            {
                await executor.RunAsync();
            }
            catch (OperationCanceledException)
            {
                RunOnUiThread(() =>
                {
                    MarkRunningSteps(runtimeState, StepRunState.Cancelled);
                    runtimeState.State = ProcessState.Stopped;
                    runtimeState.StartTime = null;
                    runtimeState.ActiveVariable = null;
                    ServiceStateChanged?.Invoke(serviceId, ProcessState.Stopped);
                });
            }
            catch (Exception ex)
            {
                RunOnUiThread(() =>
                {
                    MarkRunningSteps(runtimeState, StepRunState.Failed, ex.Message);
                    runtimeState.State = ProcessState.StartFailed;
                    runtimeState.LastError = ex.Message;
                    runtimeState.StartTime = null;
                    runtimeState.ActiveVariable = null;
                    ServiceOutput?.Invoke(serviceId, new LogEntry(LogLevel.Error, ex.Message, "system"));
                    ServiceStateChanged?.Invoke(serviceId, ProcessState.StartFailed);
                });
            }
            finally
            {
                lock (_gate)
                {
                    if (_executors.TryGetValue(serviceId, out var currentExecutor) && ReferenceEquals(currentExecutor, executor))
                        _executors.Remove(serviceId);
                    if (_cancellationTokens.TryGetValue(serviceId, out var currentCts) && ReferenceEquals(currentCts, cts))
                        _cancellationTokens.Remove(serviceId);
                }

                CompleteIdleServiceState(serviceId);
            }
        });

        return true;
    }

    public async Task StopServiceAsync(Guid serviceId)
    {
        CancellationTokenSource? cts;
        ScriptExecutor? executor;
        ServiceRuntimeState? state;
        List<KeyValuePair<(Guid ServiceId, Guid StepId), ScriptExecutor>> stepExecutors;
        List<KeyValuePair<(Guid ServiceId, Guid StepId), CancellationTokenSource>> stepTokens;

        lock (_gate)
        {
            _cancellationTokens.TryGetValue(serviceId, out cts);
            _executors.TryGetValue(serviceId, out executor);
            _runtimeStates.TryGetValue(serviceId, out state);
            stepExecutors = _stepExecutors.Where(kvp => kvp.Key.ServiceId == serviceId).ToList();
            stepTokens = _stepCancellationTokens.Where(kvp => kvp.Key.ServiceId == serviceId).ToList();
        }

        if (state != null && state.State is ProcessState.Running or ProcessState.Starting)
        {
            RunOnUiThread(() =>
            {
                state.State = ProcessState.Stopping;
                ServiceStateChanged?.Invoke(serviceId, ProcessState.Stopping);
            });
        }

        SafeCancel(cts);
        foreach (var token in stepTokens)
            SafeCancel(token.Value);

        if (executor != null)
            await executor.StopAsync();

        foreach (var stepExecutor in stepExecutors)
            await stepExecutor.Value.StopAsync();

        if (state != null)
        {
            RunOnUiThread(() =>
            {
                MarkRunningSteps(state, StepRunState.Cancelled);
                state.State = ProcessState.Stopped;
                state.StartTime = null;
                state.ActiveVariable = null;
                ServiceStateChanged?.Invoke(serviceId, ProcessState.Stopped);
            });
        }
    }

    public async Task RestartServiceAsync(Guid serviceId, ServiceStartOptions? options = null)
    {
        await StopServiceAsync(serviceId);
        await Task.Delay(500);
        StartService(serviceId, options);
    }

    public bool RunStep(Guid serviceId, Guid stepId, string? variable = null)
    {
        ServiceRuntimeState? state;
        ScriptStep? step;
        bool promoteServiceState;
        lock (_gate)
        {
            if (!_runtimeStates.TryGetValue(serviceId, out state))
                return false;

            step = state.Config.ScriptSteps.FirstOrDefault(s => s.Id == stepId);
            if (step == null || string.IsNullOrWhiteSpace(step.Content))
                return false;

            EnsureStepStates(state);
            if (state.StepStates.TryGetValue(stepId, out var stepState) && stepState.State == StepRunState.Running)
                return false;

            var key = (serviceId, stepId);
            if (_stepExecutors.ContainsKey(key))
                return false;

            promoteServiceState = !_executors.ContainsKey(serviceId) &&
                                  state.State is ProcessState.Stopped or ProcessState.Error or ProcessState.StartFailed or ProcessState.Completed;
        }

        if (promoteServiceState)
        {
            RunOnUiThread(() =>
            {
                state.State = ProcessState.Running;
                state.StartTime = DateTime.Now;
                state.LastError = null;
                state.ActiveVariable = step.UseVariable ? variable : null;
                ServiceStateChanged?.Invoke(serviceId, ProcessState.Running);
            });
        }

        var cts = new CancellationTokenSource();
        var executor = new ScriptExecutor(state.Config, cts.Token, new ServiceStartOptions { OnlyStepId = stepId, Variable = variable });
        var executorKey = (serviceId, stepId);

        lock (_gate)
        {
            _stepCancellationTokens[executorKey] = cts;
            _stepExecutors[executorKey] = executor;
        }

        WireExecutor(serviceId, state, executor, updateServiceState: false);

        _ = Task.Run(async () =>
        {
            try
            {
                await executor.RunSingleStepAsync(stepId, variable);
            }
            catch (OperationCanceledException)
            {
                RunOnUiThread(() => UpdateStepState(serviceId, new StepRuntimeState
                {
                    StepId = stepId,
                    StepName = step?.Name ?? string.Empty,
                    State = StepRunState.Cancelled,
                    ActiveVariable = variable,
                    EndedAt = DateTime.Now
                }));
            }
            catch (Exception ex)
            {
                RunOnUiThread(() =>
                {
                    UpdateStepState(serviceId, new StepRuntimeState
                    {
                        StepId = stepId,
                        StepName = step?.Name ?? string.Empty,
                        State = StepRunState.Failed,
                        ActiveVariable = variable,
                        Error = ex.Message,
                        EndedAt = DateTime.Now
                    });
                    ServiceOutput?.Invoke(serviceId, new LogEntry(LogLevel.Error, ex.Message, "system", step?.Name));
                });
            }
            finally
            {
                lock (_gate)
                {
                    if (_stepExecutors.TryGetValue(executorKey, out var currentExecutor) && ReferenceEquals(currentExecutor, executor))
                        _stepExecutors.Remove(executorKey);
                    if (_stepCancellationTokens.TryGetValue(executorKey, out var currentCts) && ReferenceEquals(currentCts, cts))
                        _stepCancellationTokens.Remove(executorKey);
                }

                CompleteIdleServiceState(serviceId);
            }
        });

        return true;
    }

    public async Task StopAllAsync()
    {
        var tasks = Snapshot()
            .Where(s => s.State is ProcessState.Running or ProcessState.Starting or ProcessState.Stopping ||
                        s.StepStates.Values.Any(step => step.State == StepRunState.Running))
            .Select(s => StopServiceAsync(s.Config.Id));

        await Task.WhenAll(tasks);
    }

    public void Dispose()
    {
        foreach (var cts in _cancellationTokens.Values.ToList())
            SafeCancel(cts);
        foreach (var cts in _stepCancellationTokens.Values.ToList())
            SafeCancel(cts);

        foreach (var executor in _executors.Values.ToList())
            executor.Dispose();
        foreach (var executor in _stepExecutors.Values.ToList())
            executor.Dispose();

        _executors.Clear();
        _cancellationTokens.Clear();
        _stepExecutors.Clear();
        _stepCancellationTokens.Clear();
    }

    private void WireExecutor(Guid serviceId, ServiceRuntimeState state, ScriptExecutor executor, bool updateServiceState)
    {
        executor.OutputReceived += entry =>
        {
            RunOnUiThread(() => ServiceOutput?.Invoke(serviceId, entry));
        };

        executor.StepStateChanged += stepState =>
        {
            RunOnUiThread(() => UpdateStepState(serviceId, stepState));
        };

        if (!updateServiceState)
            return;

        executor.StateChanged += newState =>
        {
            RunOnUiThread(() =>
            {
                state.State = newState;
                state.StartTime = newState == ProcessState.Running ? DateTime.Now : state.StartTime;

                if (newState is ProcessState.Stopped or ProcessState.Error or ProcessState.StartFailed or ProcessState.Completed)
                {
                    state.StartTime = null;
                    state.ActiveVariable = null;
                }

                ServiceStateChanged?.Invoke(serviceId, newState);
            });
        };
    }

    private void UpdateStepState(Guid serviceId, StepRuntimeState update)
    {
        ServiceRuntimeState? service;
        lock (_gate)
        {
            if (!_runtimeStates.TryGetValue(serviceId, out service))
                return;

            if (!service.StepStates.TryGetValue(update.StepId, out var current))
            {
                current = new StepRuntimeState { StepId = update.StepId, StepName = update.StepName };
                service.StepStates[update.StepId] = current;
            }

            current.StepName = string.IsNullOrWhiteSpace(update.StepName) ? current.StepName : update.StepName;
            current.State = update.State;
            current.ExitCode = update.ExitCode;
            current.ActiveVariable = update.ActiveVariable;
            current.Error = update.Error;
            if (update.StartedAt.HasValue)
                current.StartedAt = update.StartedAt;
            if (update.EndedAt.HasValue)
                current.EndedAt = update.EndedAt;
            if (update.State == StepRunState.Running)
            {
                current.EndedAt = null;
                current.Error = null;
            }
        }

        StepStateChanged?.Invoke(serviceId, update);
    }

    private void CompleteIdleServiceState(Guid serviceId)
    {
        ServiceRuntimeState? state;
        bool hasMainExecutor;
        bool hasStepExecutor;
        DateTime? runStartedAt;
        lock (_gate)
        {
            if (!_runtimeStates.TryGetValue(serviceId, out state))
                return;

            hasMainExecutor = _executors.ContainsKey(serviceId);
            hasStepExecutor = _stepExecutors.Keys.Any(key => key.ServiceId == serviceId);
            runStartedAt = state.StartTime;
        }

        if (hasMainExecutor || hasStepExecutor)
            return;

        if (state.StepStates.Values.Any(step => step.State == StepRunState.Running))
            return;

        if (state.State is ProcessState.Stopped or ProcessState.Completed or ProcessState.StartFailed or ProcessState.Error)
            return;

        var relevantSteps = state.StepStates.Values
            .Where(step => !runStartedAt.HasValue ||
                           (step.StartedAt.HasValue && step.StartedAt.Value >= runStartedAt.Value) ||
                           (step.EndedAt.HasValue && step.EndedAt.Value >= runStartedAt.Value))
            .ToList();

        var nextState = (state.State == ProcessState.Stopping ||
                         state.State == ProcessState.Stopped && relevantSteps.Any(step => step.State == StepRunState.Cancelled))
            ? ProcessState.Stopped
            : relevantSteps.Any(step => step.State == StepRunState.Failed)
            ? ProcessState.StartFailed
            : ProcessState.Completed;

        RunOnUiThread(() =>
        {
            state.State = nextState;
            state.StartTime = null;
            state.ActiveVariable = null;
            ServiceStateChanged?.Invoke(serviceId, nextState);
        });
    }

    private static void EnsureStepStates(ServiceRuntimeState state)
    {
        var validIds = state.Config.ScriptSteps.Select(s => s.Id).ToHashSet();
        foreach (var stale in state.StepStates.Keys.Where(id => !validIds.Contains(id)).ToList())
            state.StepStates.Remove(stale);

        foreach (var step in state.Config.ScriptSteps)
        {
            if (!state.StepStates.ContainsKey(step.Id))
            {
                state.StepStates[step.Id] = new StepRuntimeState
                {
                    StepId = step.Id,
                    StepName = step.Name,
                    State = string.IsNullOrWhiteSpace(step.Content) ? StepRunState.Skipped : StepRunState.NotRun
                };
            }
            else
            {
                state.StepStates[step.Id].StepName = step.Name;
                if (string.IsNullOrWhiteSpace(step.Content) && state.StepStates[step.Id].State != StepRunState.Running)
                    state.StepStates[step.Id].State = StepRunState.Skipped;
            }
        }
    }

    private static void ResetStepStatesForRun(ServiceRuntimeState state, string? variable)
    {
        EnsureStepStates(state);
        foreach (var step in state.Config.ScriptSteps)
        {
            state.StepStates[step.Id] = new StepRuntimeState
            {
                StepId = step.Id,
                StepName = step.Name,
                State = string.IsNullOrWhiteSpace(step.Content) || !step.RunOnStart ? StepRunState.Skipped : StepRunState.NotRun,
                ActiveVariable = string.IsNullOrWhiteSpace(step.Content) || !step.RunOnStart || !step.UseVariable ? null : variable
            };
        }
    }

    private void MarkRunningSteps(ServiceRuntimeState state, StepRunState nextState, string? error = null)
    {
        foreach (var stepState in state.StepStates.Values.Where(s => s.State == StepRunState.Running).ToList())
        {
            stepState.State = nextState;
            stepState.Error = error;
            stepState.EndedAt = DateTime.Now;
            StepStateChanged?.Invoke(state.Config.Id, stepState);
        }
    }

    private static void SafeCancel(CancellationTokenSource? cts)
    {
        if (cts == null)
            return;

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }
}
