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

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    public string ConfigDirectory => ConfigDir;
    public string PathToConfig => ConfigPath;
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

        if (!File.Exists(ConfigPath))
            return new AppConfig();

        try
        {
            var json = await File.ReadAllTextAsync(ConfigPath);
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
