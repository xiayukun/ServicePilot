namespace ServicePilot.Models;

public class ServiceConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public List<ScriptStep> ScriptSteps { get; set; } = new();

    /// <summary>
    /// Legacy v1 service-level variables. Kept only for migrating old config files; not used by the v2 model
    /// (variables now live on individual actions via <see cref="ScriptStep.StepVariables"/>).
    /// </summary>
    public List<string> PresetVariables { get; set; } = new();

    public bool AutoStart { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class ServiceTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<ScriptStep> ScriptSteps { get; set; } = new();

    /// <summary>
    /// Legacy v1 template-level variables. Kept only for migration; not used by the v2 model.
    /// </summary>
    public List<string> PresetVariables { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

public class ServiceStartOptions
{
    public string? Variable { get; set; }

    /// <summary>Run a single action standalone.</summary>
    public Guid? OnlyStepId { get; set; }

    /// <summary>Run a specific composite action. When null, the first composite action is used.</summary>
    public Guid? CompositeStepId { get; set; }

    public static ServiceStartOptions Empty { get; } = new();
}

public class AppConfig
{
    public int Version { get; set; } = 2;
    public List<ServiceConfig> Services { get; set; } = new();
    public List<ServiceTemplate> ServiceTemplates { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
}

public class AppSettings
{
    public string Language { get; set; } = "auto";
    public bool BuiltInTemplatesSeeded { get; set; }
}
