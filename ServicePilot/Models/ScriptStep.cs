using System.Text.Json.Serialization;

namespace ServicePilot.Models;

public enum ScriptType
{
    Batch,
    PowerShell,
    Python,
    Node
}

public enum StepKind
{
    Action,
    Composite
}

public class ScriptStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    public StepKind Kind { get; set; } = StepKind.Action;

    public ScriptType ScriptType { get; set; } = ScriptType.Batch;
    public bool UseVariable { get; set; } = true;
    public bool OpenLogOnRun { get; set; }
    public List<string> StepVariables { get; set; } = new();
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Optional C# merge script evaluated by <c>LogMergeService</c> to transform log output at runtime.
    /// When set, the script receives <see cref="MergeScriptGlobals"/> and returns a <see cref="MergeResult"/>.
    /// </summary>
    public string? LogMergeScript { get; set; }

    /// <summary>
    /// For <see cref="StepKind.Composite"/> steps: ordered ids of the member actions to run in sequence.
    /// </summary>
    public List<Guid> MemberStepIds { get; set; } = new();

    public int Order { get; set; }

    /// <summary>
    /// Legacy v1 flag kept only for migrating old config files. Not used by the v2 model.
    /// </summary>
    public bool RunOnStart { get; set; } = true;

    [JsonIgnore]
    public bool IsComposite => Kind == StepKind.Composite;

    [JsonIgnore]
    public string DisplayLabel { get; set; } = string.Empty;
}
