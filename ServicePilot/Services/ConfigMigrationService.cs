using ServicePilot.Models;

namespace ServicePilot.Services;

/// <summary>
/// Migrates legacy v1 configuration (service-level preset variables + RunOnStart steps)
/// into the v2 model where steps are either an action (with a command) or a composite action
/// (which runs an ordered set of member actions).
/// </summary>
public static class ConfigMigrationService
{
    /// <summary>Name given to the composite action created from the old "run on start" steps.</summary>
    public const string StartCompositeName = "启动";

    public static AppConfig Migrate(AppConfig legacy)
    {
        foreach (var service in legacy.Services)
        {
            service.ScriptSteps = MigrateSteps(service.ScriptSteps, service.PresetVariables);
            service.PresetVariables = new List<string>();
        }

        foreach (var template in legacy.ServiceTemplates)
        {
            template.ScriptSteps = MigrateSteps(template.ScriptSteps, template.PresetVariables);
            template.PresetVariables = new List<string>();
        }

        legacy.Version = 2;
        return legacy;
    }

    private static List<ScriptStep> MigrateSteps(List<ScriptStep> steps, List<string> presetVariables)
    {
        var ordered = steps.OrderBy(s => s.Order).ToList();

        foreach (var step in ordered)
        {
            step.Kind = StepKind.Action;
            step.MemberStepIds ??= new List<Guid>();

            // Old startup steps consumed service-level preset variables; move them onto the action itself.
            if (step.RunOnStart && step.UseVariable && presetVariables.Count > 0)
                step.StepVariables = MergeVariables(step.StepVariables, presetVariables);
        }

        var startupMembers = ordered
            .Where(s => s.RunOnStart && !string.IsNullOrWhiteSpace(s.Content))
            .Select(s => s.Id)
            .ToList();

        var result = new List<ScriptStep>();

        if (startupMembers.Count > 0)
        {
            result.Add(new ScriptStep
            {
                Id = Guid.NewGuid(),
                Name = StartCompositeName,
                Kind = StepKind.Composite,
                UseVariable = false,
                OpenLogOnRun = false,
                Content = string.Empty,
                MemberStepIds = startupMembers,
                Order = 0
            });
        }

        var nextOrder = result.Count;
        foreach (var step in ordered)
        {
            step.Order = nextOrder++;
            result.Add(step);
        }

        return result;
    }

    private static List<string> MergeVariables(List<string> existing, List<string> incoming)
    {
        var merged = new List<string>(existing ?? new List<string>());
        foreach (var value in incoming)
        {
            if (!merged.Contains(value, StringComparer.Ordinal))
                merged.Add(value);
        }

        return merged;
    }
}
