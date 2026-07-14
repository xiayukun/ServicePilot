using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ServicePilot.Models;

namespace ServicePilot.Services;

public enum ImportConflictMode
{
    Rename,    // Default: always create new, rename on name conflict (v2.2 behavior)
    Overwrite, // Overwrite existing template by Id match
    Skip       // Skip templates with conflicting Id or name
}

public sealed class ImportedTemplateInfo
{
    public required ServiceTemplate Template { get; init; }
    public required ImportConflictMode Mode { get; init; }
    public required bool WasRenamed { get; init; }
    public string? OriginalName { get; init; }
    public Guid? ReplacedId { get; init; }
}

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

    public static async Task<(List<ImportedTemplateInfo> Imported, List<ServiceTemplate> Skipped)> ImportAsync(
        string path, IReadOnlyCollection<ServiceTemplate> existingTemplates, ImportConflictMode conflictMode = ImportConflictMode.Rename)
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

        var existingById = existingTemplates
            .Where(t => t.Id != Guid.Empty)
            .ToDictionary(t => t.Id);

        var imported = new List<ImportedTemplateInfo>();
        var skipped = new List<ServiceTemplate>();

        foreach (var template in templates)
        {
            switch (conflictMode)
            {
                case ImportConflictMode.Overwrite:
                {
                    var matchById = template.Id != Guid.Empty && existingById.TryGetValue(template.Id, out var byId) ? byId : null;
                    var matchByName = existingTemplates.FirstOrDefault(t =>
                        string.Equals(t.Name, template.Name, StringComparison.OrdinalIgnoreCase));

                    var target = matchById ?? matchByName;
                    if (target != null)
                    {
                        var replacedId = target.Id;
                        ApplyImportedFields(target, template);
                        imported.Add(new ImportedTemplateInfo
                        {
                            Template = target,
                            Mode = ImportConflictMode.Overwrite,
                            WasRenamed = false,
                            OriginalName = template.Name,
                            ReplacedId = replacedId
                        });
                        usedNames.Add(target.Name);
                    }
                    else
                    {
                        var normalized = NormalizeImportedTemplate(template, usedNames);
                        imported.Add(new ImportedTemplateInfo
                        {
                            Template = normalized,
                            Mode = ImportConflictMode.Rename,
                            WasRenamed = normalized.Name != template.Name?.Trim(),
                            OriginalName = template.Name,
                        });
                        usedNames.Add(normalized.Name);
                    }
                    break;
                }

                case ImportConflictMode.Skip:
                {
                    var hasIdConflict = template.Id != Guid.Empty && existingById.ContainsKey(template.Id);
                    var hasNameConflict = usedNames.Contains(template.Name?.Trim() ?? "");

                    if (hasIdConflict || hasNameConflict)
                    {
                        skipped.Add(template);
                    }
                    else
                    {
                        var normalized = NormalizeImportedTemplate(template, usedNames);
                        imported.Add(new ImportedTemplateInfo
                        {
                            Template = normalized,
                            Mode = ImportConflictMode.Rename,
                            WasRenamed = normalized.Name != template.Name?.Trim(),
                            OriginalName = template.Name,
                        });
                        usedNames.Add(normalized.Name);
                    }
                    break;
                }

                default: // Rename
                {
                    var normalized = NormalizeImportedTemplate(template, usedNames);
                    imported.Add(new ImportedTemplateInfo
                    {
                        Template = normalized,
                        Mode = ImportConflictMode.Rename,
                        WasRenamed = normalized.Name != template.Name?.Trim(),
                        OriginalName = template.Name,
                    });
                    usedNames.Add(normalized.Name);
                    break;
                }
            }
        }

        return (imported, skipped);
    }

    public static string ResolvedPath(string path) => Path.GetFullPath(path);

    private static void ApplyImportedFields(ServiceTemplate target, ServiceTemplate source)
    {
        target.Name = source.Name?.Trim() ?? target.Name;
        target.Description = source.Description ?? target.Description;
        target.UpdatedAt = DateTime.Now;
        target.ScriptSteps = CloneImportedSteps(source.ScriptSteps);
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
            PresetVariables = [],
            ScriptSteps = CloneImportedSteps(source.ScriptSteps)
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
                Kind = step.Kind,
                ScriptType = step.ScriptType,
                UseVariable = step.UseVariable,
                OpenLogOnRun = step.OpenLogOnRun,
                StepVariables = step.StepVariables.ToList(),
                Content = step.Content,
                MemberStepIds = step.MemberStepIds.ToList(),
                Order = step.Order
            })
            .ToList()
    };

    private static List<ScriptStep> CloneImportedSteps(IEnumerable<ScriptStep> sourceSteps)
    {
        var ordered = sourceSteps.OrderBy(step => step.Order).ToList();
        var idMap = ordered.ToDictionary(step => step.Id, _ => Guid.NewGuid());
        var result = new List<ScriptStep>();

        for (var index = 0; index < ordered.Count; index++)
        {
            var step = ordered[index];
            var kind = step.Kind;
            var name = string.IsNullOrWhiteSpace(step.Name)
                ? LocalizationService.Current.F("DefaultStepName", index + 1)
                : step.Name.Trim();
            result.Add(new ScriptStep
            {
                Id = idMap[step.Id],
                Name = name,
                Kind = kind,
                ScriptType = step.ScriptType,
                UseVariable = kind == StepKind.Action && step.UseVariable,
                OpenLogOnRun = kind == StepKind.Action && step.OpenLogOnRun,
                StepVariables = kind == StepKind.Action ? DistinctStrings(step.StepVariables) : [],
                Content = kind == StepKind.Action ? step.Content ?? string.Empty : string.Empty,
                MemberStepIds = kind == StepKind.Composite
                    ? step.MemberStepIds.Where(idMap.ContainsKey).Select(id => idMap[id]).ToList()
                    : [],
                Order = index
            });
        }

        return result;
    }

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
