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

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<AppConfig> LoadAsync()
    {
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
}
