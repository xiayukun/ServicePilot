using ServicePilot.Models;

namespace ServicePilot.Services;

public static class ScriptDefinitionService
{
    public const string VariableEnvironmentName = "SERVICEPILOT_VARIABLE";

    public static ScriptStep CloneStep(ScriptStep source)
    {
        return new ScriptStep
        {
            Id = Guid.NewGuid(),
            Name = source.Name,
            ScriptType = source.ScriptType,
            UseVariable = source.UseVariable,
            RunOnStart = source.RunOnStart,
            OpenLogOnRun = source.OpenLogOnRun,
            StepVariables = source.StepVariables.ToList(),
            Content = source.Content,
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

    public static ServiceTemplate CreateTemplateFromService(ServiceConfig service, string name, string description)
    {
        return new ServiceTemplate
        {
            Name = name,
            Description = description,
            PresetVariables = service.PresetVariables.ToList(),
            ScriptSteps = service.ScriptSteps
                .OrderBy(s => s.Order)
                .Select(CloneStep)
                .Select((step, index) =>
                {
                    step.Order = index;
                    return step;
                })
                .ToList()
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
            PresetVariables = template.PresetVariables.ToList(),
            ScriptSteps = template.ScriptSteps
                .OrderBy(s => s.Order)
                .Select(CloneStep)
                .Select((step, index) =>
                {
                    step.Order = index;
                    return step;
                })
                .ToList()
        };
    }

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

    private static ScriptStep CloneStepPreserveId(ScriptStep source)
    {
        var clone = CloneStep(source);
        clone.Id = source.Id;
        return clone;
    }
}
