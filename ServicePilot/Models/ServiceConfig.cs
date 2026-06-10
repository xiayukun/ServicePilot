namespace ServicePilot.Models;

public class ServiceConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public List<ScriptStep> ScriptSteps { get; set; } = new();
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
    public List<string> PresetVariables { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

public class ServiceStartOptions
{
    public string? Variable { get; set; }
    public Guid? OnlyStepId { get; set; }

    public static ServiceStartOptions Empty { get; } = new();
}

public class AppConfig
{
    public int Version { get; set; } = 1;
    public List<ServiceConfig> Services { get; set; } = new();
    public List<ServiceTemplate> ServiceTemplates { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
}

public class AppSettings
{
    public string Language { get; set; } = "auto";
}
