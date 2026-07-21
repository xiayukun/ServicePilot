using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ServicePilot.Models;

namespace ServicePilot.Services;

public class ConfigService : IDisposable
{
    private static readonly string ConfigDir =
        Environment.GetEnvironmentVariable("SERVICEPILOT_CONFIG_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ServicePilot");

    // v2 active config file. The legacy v1 file (config.json) is kept untouched after a one-time migration.
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.v2.json");
    private static readonly string LegacyV1ConfigPath = Path.Combine(ConfigDir, "config.json");

    public string ConfigDirectory => ConfigDir;
    public string PathToConfig => ConfigPath;
    public string LegacyV1Config => LegacyV1ConfigPath;
    public string LegacyLocalConfigPath => Path.Combine(AppContext.BaseDirectory, "config.json");

    // ── External change detection ──────────────────────────────────────────────
    // Raised (debounced) when config.v2.json changes on disk from an external editor.
    // Writes performed by this process are suppressed so we never reload our own save.
    public event Action? ExternalConfigChanged;

    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _debounceTimer;
    private readonly object _watchLock = new();
    private DateTime _lastSelfWriteUtc = DateTime.MinValue;
    private bool _disposed;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<AppConfig> LoadAsync()
    {
        MigrateLegacyLocalConfigIfNeeded("config.json");
        MigrateLegacyLocalConfigIfNeeded("variable-usage-cache.json");

        // Already migrated: load the v2 file directly.
        if (File.Exists(ConfigPath))
        {
            var config = await ReadConfigAsync(ConfigPath);
            NormalizeOrder(config);
            return config;
        }

        // One-time migration: read the legacy v1 config, migrate to v2, and persist to the v2 file.
        // The legacy config.json is intentionally left in place as a backup.
        if (File.Exists(LegacyV1ConfigPath))
        {
            var legacy = await ReadConfigAsync(LegacyV1ConfigPath);
            var migrated = ConfigMigrationService.Migrate(legacy);
            NormalizeOrder(migrated);
            await SaveAsync(migrated);
            return migrated;
        }

        return new AppConfig();
    }

    private static async Task<AppConfig> ReadConfigAsync(string path)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<AppConfig>(json, Options) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public async Task SaveAsync(AppConfig config)
    {
        // Clean up dangling composite member references before saving
        PurgeDanglingMembers(config);

        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(config, Options);
        var tempPath = ConfigPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json);

        // Mark this as a self-write so the FileSystemWatcher does not treat it as an external edit.
        lock (_watchLock)
            _lastSelfWriteUtc = DateTime.UtcNow;

        File.Move(tempPath, ConfigPath, overwrite: true);

        // The move produces a second timestamp; refresh the marker after the write completes.
        lock (_watchLock)
            _lastSelfWriteUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Starts watching config.v2.json for external edits. Debounced changes raise
    /// <see cref="ExternalConfigChanged"/> on a background thread. Writes performed by this
    /// process (via <see cref="SaveAsync"/>) are ignored.
    /// </summary>
    public void StartWatching()
    {
        if (_watcher != null)
            return;

        try
        {
            Directory.CreateDirectory(ConfigDir);
            _watcher = new FileSystemWatcher(ConfigDir, "config.v2.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnConfigFileEvent;
            _watcher.Created += OnConfigFileEvent;
            _watcher.Renamed += OnConfigFileEvent;
        }
        catch
        {
            // If the watcher cannot start (permissions, network path, etc.) we simply
            // fall back to no auto-reload; the app keeps working.
            _watcher = null;
        }
    }

    private void OnConfigFileEvent(object sender, FileSystemEventArgs e)
    {
        // Ignore events triggered by our own SaveAsync within a short window.
        lock (_watchLock)
        {
            if ((DateTime.UtcNow - _lastSelfWriteUtc).TotalMilliseconds < 1500)
                return;
        }

        // Debounce: editors may fire multiple events per save.
        lock (_watchLock)
        {
            _debounceTimer ??= new System.Threading.Timer(_ => RaiseExternalChanged());
            _debounceTimer.Change(400, System.Threading.Timeout.Infinite);
        }
    }

    private void RaiseExternalChanged()
    {
        // Re-check the self-write guard: a save may have landed during the debounce window.
        lock (_watchLock)
        {
            if ((DateTime.UtcNow - _lastSelfWriteUtc).TotalMilliseconds < 1500)
                return;
        }

        try
        {
            ExternalConfigChanged?.Invoke();
        }
        catch
        {
            // Never let a reload handler exception escape the timer thread.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_watcher != null)
        {
            _watcher.Changed -= OnConfigFileEvent;
            _watcher.Created -= OnConfigFileEvent;
            _watcher.Renamed -= OnConfigFileEvent;
            _watcher.Dispose();
            _watcher = null;
        }
        _debounceTimer?.Dispose();
        _debounceTimer = null;
    }

    /// <summary>
    /// Removes member ids from composite steps that reference non-existent steps within the same service/template.
    /// </summary>
    private static void PurgeDanglingMembers(AppConfig config)
    {
        foreach (var service in config.Services)
            PurgeDanglingMembers(service.ScriptSteps);

        foreach (var template in config.ServiceTemplates)
            PurgeDanglingMembers(template.ScriptSteps);
    }

    private static void PurgeDanglingMembers(List<ScriptStep> steps)
    {
        var validIds = new HashSet<Guid>(steps.Select(s => s.Id));
        foreach (var composite in steps.Where(s => s.Kind == StepKind.Composite))
            composite.MemberStepIds.RemoveAll(id => !validIds.Contains(id));
    }

    /// <summary>
    /// Normalizes the Order property of all ScriptSteps across services and templates.
    /// Sorts by Order then reassigns 0-based incrementing values, fixing negative,
    /// duplicate, or non-continuous order numbers that may result from manual JSON editing.
    /// </summary>
    private static void NormalizeOrder(AppConfig config)
    {
        foreach (var service in config.Services)
            NormalizeStepOrder(service.ScriptSteps);

        foreach (var template in config.ServiceTemplates)
            NormalizeStepOrder(template.ScriptSteps);
    }

    private static void NormalizeStepOrder(List<ScriptStep> steps)
    {
        if (steps == null || steps.Count == 0)
            return;

        var ordered = steps.OrderBy(s => s.Order).ToList();
        for (int i = 0; i < ordered.Count; i++)
            ordered[i].Order = i;
    }

    private static void MigrateLegacyLocalConfigIfNeeded(string fileName)
    {
        Directory.CreateDirectory(ConfigDir);

        var targetPath = Path.Combine(ConfigDir, fileName);
        if (File.Exists(targetPath))
            return;

        foreach (var legacyPath in GetLegacyLocalPaths(fileName))
        {
            if (!File.Exists(legacyPath))
                continue;

            try
            {
                File.Copy(legacyPath, targetPath, overwrite: false);
                return;
            }
            catch
            {
                return;
            }
        }
    }

    private static IEnumerable<string> GetLegacyLocalPaths(string fileName)
    {
        var paths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(Environment.CurrentDirectory, fileName)
        };

        return paths
            .Where(path => !string.Equals(Path.GetFullPath(path), Path.GetFullPath(Path.Combine(ConfigDir, fileName)), StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
