using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ServicePilot.Models;

namespace ServicePilot.Services;

public class ConfigService
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
            return await ReadConfigAsync(ConfigPath);

        // One-time migration: read the legacy v1 config, migrate to v2, and persist to the v2 file.
        // The legacy config.json is intentionally left in place as a backup.
        if (File.Exists(LegacyV1ConfigPath))
        {
            var legacy = await ReadConfigAsync(LegacyV1ConfigPath);
            var migrated = ConfigMigrationService.Migrate(legacy);
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
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(config, Options);
        var tempPath = ConfigPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, ConfigPath, overwrite: true);
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
