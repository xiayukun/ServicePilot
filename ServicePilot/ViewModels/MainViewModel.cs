using System.Collections.ObjectModel;
using System.Windows.Input;
using ServicePilot.Models;
using ServicePilot.Services;
using ServicePilot.ViewModels.Base;

namespace ServicePilot.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly ProcessManager _processManager;
    private readonly ConfigService _configService;
    private readonly AppConfig _appConfig;

    public MainViewModel(ProcessManager processManager, ConfigService configService, AppConfig appConfig)
    {
        _processManager = processManager;
        _configService = configService;
        _appConfig = appConfig;

        Services = new ObservableCollection<ServiceItemViewModel>();
        foreach (var state in processManager.Services)
            Services.Add(new ServiceItemViewModel(state, processManager));

        _processManager.ServiceStateChanged += (id, newState) =>
        {
            var vm = Services.FirstOrDefault(s => s.Config.Id == id);
            if (vm == null)
                return;

            vm.State = newState;
            vm.StartTime = _processManager.Services.FirstOrDefault(s => s.Config.Id == id)?.StartTime;
        };

        AddServiceCommand = new RelayCommand(() => AddServiceRequested?.Invoke());
        StopAllCommand = new AsyncRelayCommand(() => _processManager.StopAllAsync());
        ExitCommand = new AsyncRelayCommand(async () =>
        {
            await _processManager.StopAllAsync();
            System.Windows.Application.Current.Shutdown();
        });
    }

    public ObservableCollection<ServiceItemViewModel> Services { get; }

    public ICommand AddServiceCommand { get; }
    public ICommand StopAllCommand { get; }
    public ICommand ExitCommand { get; }

    public event Action? AddServiceRequested;
    public event Action<ServiceItemViewModel>? ServiceAdded;
    public event Action<Guid>? ServiceRemoved;

    public async Task SaveConfigAsync()
    {
        _appConfig.Services = Services.Select(s => s.Config).ToList();
        await _configService.SaveAsync(_appConfig);
    }

    /// <summary>
    /// Reconciles the Services view-model collection with the current ProcessManager runtime states
    /// after an external config reload. Adds view-models for new services, drops ones removed on disk,
    /// and refreshes bindings for existing ones — without recreating in-place running view-models.
    /// </summary>
    public void SyncFromRuntime(Action<ServiceItemViewModel> attachLogHandler)
    {
        var runtimeStates = _processManager.Services.ToList();
        var runtimeIds = new HashSet<Guid>(runtimeStates.Select(s => s.Config.Id));

        // Drop view-models whose service no longer exists.
        for (var i = Services.Count - 1; i >= 0; i--)
        {
            if (!runtimeIds.Contains(Services[i].Config.Id))
                Services.RemoveAt(i);
        }

        // Add / refresh.
        foreach (var state in runtimeStates)
        {
            var existing = Services.FirstOrDefault(s => s.Config.Id == state.Config.Id);
            if (existing == null)
            {
                var vm = new ServiceItemViewModel(state, _processManager);
                attachLogHandler(vm);
                Services.Add(vm);
            }
            else
            {
                existing.RefreshConfig();
            }
        }
    }

    public ServiceItemViewModel AddService(ServiceConfig config)
    {
        config.SortOrder = Services.Count;
        _appConfig.Services.Add(config);
        _processManager.AddService(config);

        var runtimeState = _processManager.Services.First(s => s.Config.Id == config.Id);
        var vm = new ServiceItemViewModel(runtimeState, _processManager);
        Services.Add(vm);
        ServiceAdded?.Invoke(vm);
        return vm;
    }

    public async Task<bool> RemoveServiceAsync(Guid serviceId)
    {
        var vm = Services.FirstOrDefault(s => s.Config.Id == serviceId);
        if (vm == null)
            return false;

        await _processManager.RemoveServiceAsync(serviceId);
        Services.Remove(vm);
        _appConfig.Services.RemoveAll(s => s.Id == serviceId);

        for (var i = 0; i < Services.Count; i++)
            Services[i].Config.SortOrder = i;

        await SaveConfigAsync();
        ServiceRemoved?.Invoke(serviceId);
        return true;
    }

    public async Task<bool> UpdateServiceAsync(ServiceConfig config)
    {
        var vm = Services.FirstOrDefault(s => s.Config.Id == config.Id);
        if (vm == null)
            return false;

        if (vm.State is ProcessState.Running or ProcessState.Starting)
            await _processManager.StopServiceAsync(config.Id);

        config.SortOrder = vm.Config.SortOrder;
        config.CreatedAt = vm.Config.CreatedAt;

        var index = _appConfig.Services.FindIndex(s => s.Id == config.Id);
        if (index >= 0)
            _appConfig.Services[index] = config;

        _processManager.UpdateService(config);
        vm.RefreshConfig();
        await SaveConfigAsync();
        return true;
    }
}
