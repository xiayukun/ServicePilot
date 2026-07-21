using ServicePilot.Models;

namespace ServicePilot.Services;

public static class ScriptDefinitionService
{
    public const string VariableEnvironmentName = "SERVICEPILOT_VARIABLE";

    public static ScriptStep CloneStep(ScriptStep source)
    {
        return new ScriptStep
        {
            Id = source.Id,
            Name = source.Name,
            Kind = source.Kind,
            ScriptType = source.ScriptType,
            UseVariable = source.UseVariable,
            OpenLogOnRun = source.OpenLogOnRun,
            StepVariables = source.StepVariables.ToList(),
            Content = source.Content,
            LogMergeScript = source.LogMergeScript,
            MemberStepIds = source.MemberStepIds.ToList(),
            Order = source.Order
        };
    }

    public static ServiceConfig CloneService(ServiceConfig source)
    {
        return new ServiceConfig
        {
            Id = source.Id,
            Name = source.Name,
            WorkingDirectory = source.WorkingDirectory,
            AutoStart = source.AutoStart,
            SortOrder = source.SortOrder,
            CreatedAt = source.CreatedAt,
            PresetVariables = source.PresetVariables.ToList(),
            ScriptSteps = source.ScriptSteps
                .OrderBy(s => s.Order)
                .Select(CloneStepPreserveId)
                .ToList()
        };
    }

    /// <summary>
    /// Clones an ordered set of steps with brand-new ids, remapping composite member references
    /// so composite actions keep pointing at the right cloned member actions.
    /// </summary>
    public static List<ScriptStep> CloneStepsWithNewIds(IEnumerable<ScriptStep> steps)
    {
        var ordered = steps.OrderBy(s => s.Order).ToList();
        var idMap = new Dictionary<Guid, Guid>();
        var clones = new List<ScriptStep>();

        foreach (var source in ordered)
        {
            var newId = Guid.NewGuid();
            idMap[source.Id] = newId;
            var clone = CloneStep(source);
            clone.Id = newId;
            clones.Add(clone);
        }

        for (var i = 0; i < clones.Count; i++)
        {
            var clone = clones[i];
            clone.Order = i;
            if (clone.Kind == StepKind.Composite)
            {
                clone.MemberStepIds = clone.MemberStepIds
                    .Where(idMap.ContainsKey)
                    .Select(id => idMap[id])
                    .ToList();
            }
        }

        return clones;
    }

    public static ServiceTemplate CreateTemplateFromService(ServiceConfig service, string name, string description)
    {
        return new ServiceTemplate
        {
            Name = name,
            Description = description,
            ScriptSteps = CloneStepsWithNewIds(service.ScriptSteps)
        };
    }

    public static ServiceConfig ApplyTemplateToService(ServiceConfig target, ServiceTemplate template)
    {
        var createdAt = target.CreatedAt == default ? DateTime.Now : target.CreatedAt;
        return new ServiceConfig
        {
            Id = target.Id,
            Name = string.IsNullOrWhiteSpace(target.Name) ? template.Name : target.Name,
            WorkingDirectory = target.WorkingDirectory,
            AutoStart = target.AutoStart,
            SortOrder = target.SortOrder,
            CreatedAt = createdAt,
            ScriptSteps = CloneStepsWithNewIds(template.ScriptSteps)
        };
    }

    /// <summary>
    /// Resolves which composite action to run: the one matching <paramref name="compositeId"/>,
    /// or the first composite action (by order) when no id is supplied.
    /// </summary>
    public static ScriptStep? ResolveComposite(ServiceConfig config, Guid? compositeId)
    {
        var composites = config.ScriptSteps
            .Where(s => s.Kind == StepKind.Composite)
            .OrderBy(s => s.Order)
            .ToList();

        if (compositeId.HasValue)
            return composites.FirstOrDefault(s => s.Id == compositeId.Value);

        return composites.FirstOrDefault();
    }

    /// <summary>Resolves the ordered runnable member actions of a composite action.</summary>
    public static List<ScriptStep> ResolveCompositeMembers(ServiceConfig config, ScriptStep composite)
    {
        var byId = new Dictionary<Guid, ScriptStep>();
        foreach (var step in config.ScriptSteps)
            byId[step.Id] = step;

        return composite.MemberStepIds
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .Where(s => s.Kind == StepKind.Action && !string.IsNullOrWhiteSpace(s.Content))
            .ToList();
    }

    /// <summary>The single member action that carries variables, if any (used to drive variable menus).</summary>
    public static ScriptStep? FindVariableMember(ServiceConfig config, ScriptStep composite) =>
        ResolveCompositeMembers(config, composite).FirstOrDefault(s => s.UseVariable);

    public static ScriptStep CreateRunnableStep(ScriptStep source, string? variable)
    {
        var clone = CloneStep(source);
        clone.Id = source.Id;
        if (clone.UseVariable)
            clone.Content = ApplyVariable(clone.Content, variable);
        return clone;
    }

    public static string ApplyVariable(string content, string? variable)
    {
        if (string.IsNullOrEmpty(variable))
            return content;

        return content
            .Replace("{{variable}}", variable, StringComparison.OrdinalIgnoreCase)
            .Replace("{{变量}}", variable, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Clones a step preserving its original id. Since <see cref="CloneStep"/> now preserves
    /// the source id by default, this method is kept for readability at call sites.
    /// </summary>
    private static ScriptStep CloneStepPreserveId(ScriptStep source) => CloneStep(source);
}
