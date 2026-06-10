using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServicePilot.Services;

public sealed class PresetVariableUsageStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private VariableUsageCache _cache = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public PresetVariableUsageStore(string directory)
    {
        _path = Path.Combine(directory, "variable-usage-cache.json");
        Load();
    }

    public string CachePath => _path;

    public IReadOnlyList<T> SortServices<T>(
        IEnumerable<T> services,
        Func<T, Guid> idSelector,
        Func<T, int> sortOrderSelector,
        Func<T, string> nameSelector)
    {
        lock (_gate)
        {
            var usage = _cache.RecentServices.ToDictionary(s => s.ServiceId, s => s);

            return services
                .Select((value, index) => new
                {
                    Value = value,
                    Index = index,
                    Usage = usage.TryGetValue(idSelector(value), out var entry) ? entry : null
                })
                .OrderByDescending(item => item.Usage?.LastUsedAt ?? DateTime.MinValue)
                .ThenBy(item => item.Usage == null ? sortOrderSelector(item.Value) : 0)
                .ThenBy(item => item.Usage == null ? nameSelector(item.Value) : string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Index)
                .Select(item => item.Value)
                .ToList();
        }
    }

    public void RememberService(Guid serviceId)
    {
        lock (_gate)
        {
            var entry = _cache.RecentServices.FirstOrDefault(s => s.ServiceId == serviceId);
            if (entry == null)
            {
                entry = new ServiceUsageEntry { ServiceId = serviceId };
                _cache.RecentServices.Add(entry);
            }

            entry.LastUsedAt = DateTime.Now;
            entry.UseCount++;
            Save();
        }
    }

    public IReadOnlyList<string> Sort(Guid serviceId, IEnumerable<string> variables)
    {
        lock (_gate)
        {
            var serviceUsage = _cache.Services.FirstOrDefault(s => s.ServiceId == serviceId);
            var usage = serviceUsage?.Variables.ToDictionary(v => v.Value, v => v, StringComparer.OrdinalIgnoreCase)
                        ?? new Dictionary<string, VariableUsageEntry>(StringComparer.OrdinalIgnoreCase);

            return variables
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select((value, index) => new
                {
                    Value = value.Trim(),
                    Index = index,
                    Entry = usage.TryGetValue(value.Trim(), out var entry) ? entry : null
                })
                .GroupBy(item => item.Value, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(item => item.Entry?.LastUsedAt ?? DateTime.MinValue)
                .ThenBy(item => item.Index)
                .Select(item => item.Value)
                .ToList();
        }
    }

    public string? First(Guid serviceId, IEnumerable<string> variables) =>
        Sort(serviceId, variables).FirstOrDefault();

    public void Remember(Guid serviceId, string? variable)
    {
        if (string.IsNullOrWhiteSpace(variable))
            return;

        lock (_gate)
        {
            var serviceUsage = _cache.Services.FirstOrDefault(s => s.ServiceId == serviceId);
            if (serviceUsage == null)
            {
                serviceUsage = new ServiceVariableUsage { ServiceId = serviceId };
                _cache.Services.Add(serviceUsage);
            }

            var normalized = variable.Trim();
            var entry = serviceUsage.Variables
                .FirstOrDefault(v => string.Equals(v.Value, normalized, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                entry = new VariableUsageEntry { Value = normalized };
                serviceUsage.Variables.Add(entry);
            }

            entry.LastUsedAt = DateTime.Now;
            entry.UseCount++;
            Save();
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;

            var json = File.ReadAllText(_path);
            _cache = JsonSerializer.Deserialize<VariableUsageCache>(json, Options) ?? new VariableUsageCache();
        }
        catch
        {
            _cache = new VariableUsageCache();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tempPath = _path + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(_cache, Options));
            File.Move(tempPath, _path, overwrite: true);
        }
        catch
        {
            // Cache writes should never block service control.
        }
    }

    private sealed class VariableUsageCache
    {
        public int Version { get; set; } = 1;
        public List<ServiceVariableUsage> Services { get; set; } = new();
        public List<ServiceUsageEntry> RecentServices { get; set; } = new();
    }

    private sealed class ServiceVariableUsage
    {
        public Guid ServiceId { get; set; }
        public List<VariableUsageEntry> Variables { get; set; } = new();
    }

    private sealed class VariableUsageEntry
    {
        public string Value { get; set; } = string.Empty;
        public DateTime LastUsedAt { get; set; }
        public int UseCount { get; set; }
    }

    private sealed class ServiceUsageEntry
    {
        public Guid ServiceId { get; set; }
        public DateTime LastUsedAt { get; set; }
        public int UseCount { get; set; }
    }
}
