using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ServicePilot.Models;

namespace ServicePilot.Services;

public sealed class TemplateExportPackage
{
    public string Format { get; set; } = "ServicePilot.TemplateExport";
    public int Version { get; set; } = 1;
    public DateTime ExportedAt { get; set; } = DateTime.Now;
    public List<ServiceTemplate> Templates { get; set; } = [];
}

public static class TemplateExchangeService
{
    public const string DefaultExtension = ".servicepilot-template.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task ExportAsync(ServiceTemplate template, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Export path is required.", nameof(path));

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var package = new TemplateExportPackage
        {
            ExportedAt = DateTime.Now,
            Templates = [CloneTemplatePreserveIds(template)]
        };

        var json = JsonSerializer.Serialize(package, JsonOptions);
        await File.WriteAllTextAsync(fullPath, json);
    }

    public static async Task<List<ServiceTemplate>> ImportAsync(string path, IReadOnlyCollection<ServiceTemplate> existingTemplates)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Import path is required.", nameof(path));

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Template file does not exist.", fullPath);

        var json = await File.ReadAllTextAsync(fullPath);
        var templates = DeserializeTemplates(json);
        if (templates.Count == 0)
            throw new InvalidDataException("Template file does not contain any templates.");

        var usedNames = existingTemplates
            .Select(t => t.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var imported = new List<ServiceTemplate>();

        foreach (var template in templates)
        {
            var normalized = NormalizeImportedTemplate(template, usedNames);
            usedNames.Add(normalized.Name);
            imported.Add(normalized);
        }

        return imported;
    }

    private static List<ServiceTemplate> DeserializeTemplates(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Object && TryGetPropertyIgnoreCase(root, "Templates", out var templatesElement))
            return templatesElement.Deserialize<List<ServiceTemplate>>(JsonOptions) ?? [];

        if (root.ValueKind == JsonValueKind.Array)
            return root.Deserialize<List<ServiceTemplate>>(JsonOptions) ?? [];

        if (root.ValueKind == JsonValueKind.Object)
        {
            var template = root.Deserialize<ServiceTemplate>(JsonOptions);
            return template == null ? [] : [template];
        }

        return [];
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static ServiceTemplate NormalizeImportedTemplate(ServiceTemplate source, HashSet<string> usedNames)
    {
        if (string.IsNullOrWhiteSpace(source.Name))
            throw new InvalidDataException("Imported template is missing a name.");

        if (source.ScriptSteps.Count == 0)
            throw new InvalidDataException($"Imported template \"{source.Name}\" has no script steps.");

        var now = DateTime.Now;
        return new ServiceTemplate
        {
            Id = Guid.NewGuid(),
            Name = MakeUniqueName(source.Name.Trim(), usedNames),
            Description = source.Description ?? string.Empty,
            CreatedAt = now,
            UpdatedAt = now,
            PresetVariables = DistinctStrings(source.PresetVariables),
            ScriptSteps = source.ScriptSteps
                .OrderBy(step => step.Order)
                .Select((step, index) => new ScriptStep
                {
                    Id = Guid.NewGuid(),
                    Name = string.IsNullOrWhiteSpace(step.Name)
                        ? LocalizationService.Current.F("DefaultStepName", index + 1)
                        : step.Name.Trim(),
                    ScriptType = step.ScriptType,
                    UseVariable = step.UseVariable,
                    RunOnStart = step.RunOnStart,
                    OpenLogOnRun = step.OpenLogOnRun,
                    StepVariables = DistinctStrings(step.StepVariables),
                    Content = step.Content ?? string.Empty,
                    Order = index
                })
                .ToList()
        };
    }

    private static ServiceTemplate CloneTemplatePreserveIds(ServiceTemplate source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Description = source.Description,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt,
        PresetVariables = source.PresetVariables.ToList(),
        ScriptSteps = source.ScriptSteps
            .OrderBy(step => step.Order)
            .Select(step => new ScriptStep
            {
                Id = step.Id,
                Name = step.Name,
                ScriptType = step.ScriptType,
                UseVariable = step.UseVariable,
                RunOnStart = step.RunOnStart,
                OpenLogOnRun = step.OpenLogOnRun,
                StepVariables = step.StepVariables.ToList(),
                Content = step.Content,
                Order = step.Order
            })
            .ToList()
    };

    private static List<string> DistinctStrings(IEnumerable<string>? values) =>
        (values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string MakeUniqueName(string baseName, HashSet<string> usedNames)
    {
        if (!usedNames.Contains(baseName))
            return baseName;

        var suffix = LocalizationService.Current.T("ImportedTemplateSuffix");
        var candidate = $"{baseName} ({suffix})";
        if (!usedNames.Contains(candidate))
            return candidate;

        for (var i = 2; ; i++)
        {
            candidate = $"{baseName} ({suffix} {i})";
            if (!usedNames.Contains(candidate))
                return candidate;
        }
    }
}
