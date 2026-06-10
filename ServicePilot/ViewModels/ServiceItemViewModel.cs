using System.Collections.ObjectModel;
using System.Windows.Input;
using ServicePilot.Models;
using ServicePilot.Services;
using ServicePilot.ViewModels.Base;

namespace ServicePilot.ViewModels;

public class ServiceItemViewModel : ViewModelBase
{
    private readonly ProcessManager _processManager;
    private ProcessState _state = ProcessState.Stopped;
    private DateTime? _startTime;

    public ServiceItemViewModel(ServiceRuntimeState runtimeState, ProcessManager processManager)
    {
        RuntimeState = runtimeState;
        _processManager = processManager;
        _state = runtimeState.State;
        _startTime = runtimeState.StartTime;

        StartCommand = new RelayCommand(() => _processManager.StartService(Config.Id),
            () => State is ProcessState.Stopped or ProcessState.Error or ProcessState.StartFailed or ProcessState.Completed);
        StopCommand = new AsyncRelayCommand(() => _processManager.StopServiceAsync(Config.Id),
            () => State is ProcessState.Running or ProcessState.Starting);
        RestartCommand = new AsyncRelayCommand(() => _processManager.RestartServiceAsync(Config.Id),
            () => State is not ProcessState.Starting and not ProcessState.Stopping);
        ViewLogCommand = new RelayCommand(() => LogRequested?.Invoke(this));
    }

    public ServiceRuntimeState RuntimeState { get; }
    public ServiceConfig Config => RuntimeState.Config;
    public string Name => Config.Name;

    public ProcessState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColor));
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public DateTime? StartTime
    {
        get => _startTime;
        set => SetProperty(ref _startTime, value);
    }

    public string StatusText => State switch
    {
        ProcessState.Stopped => LocalizationService.Current.T("StateStopped"),
        ProcessState.Starting => LocalizationService.Current.T("StateStarting"),
        ProcessState.Running => LocalizationService.Current.T("StateRunning"),
        ProcessState.Stopping => LocalizationService.Current.T("StateStopping"),
        ProcessState.Error => LocalizationService.Current.T("StateError"),
        ProcessState.StartFailed => LocalizationService.Current.T("StateStartFailed"),
        ProcessState.Completed => LocalizationService.Current.T("StateCompleted"),
        _ => LocalizationService.Current.T("StateUnknown")
    };

    public string StatusColor => State switch
    {
        ProcessState.Running => "#4CAF50",
        ProcessState.Starting => "#FF9800",
        ProcessState.Error => "#F44336",
        ProcessState.StartFailed => "#F44336",
        ProcessState.Stopping => "#FF9800",
        ProcessState.Completed => "#2196F3",
        _ => "#9E9E9E"
    };

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RestartCommand { get; }
    public ICommand ViewLogCommand { get; }

    public event Action<ServiceItemViewModel>? LogRequested;

    public void RefreshConfig()
    {
        OnPropertyChanged(nameof(Config));
        OnPropertyChanged(nameof(Name));
    }

    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(StatusText));
    }
}
