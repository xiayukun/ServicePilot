using System.Text.Json.Serialization;

namespace ServicePilot.Models;

public enum ScriptType
{
    Batch,
    PowerShell,
    Python,
    Node
}

public class ScriptStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public ScriptType ScriptType { get; set; } = ScriptType.Batch;
    public bool UseVariable { get; set; } = true;
    public bool RunOnStart { get; set; } = true;
    public List<string> StepVariables { get; set; } = new();
    public string Content { get; set; } = string.Empty;
    public int Order { get; set; }

    [JsonIgnore]
    public string DisplayLabel { get; set; } = string.Empty;
}
