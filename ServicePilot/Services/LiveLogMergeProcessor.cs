using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ServicePilot.Models;

namespace ServicePilot.Services;

/// <summary>
/// Evaluates only notification-capable merge scripts against the live stream. This keeps existing
/// folding-only scripts on the log-window hot path, while allowing Notify(...) to work when no log
/// window is open. Results are attached transiently to each entry so an open log window does not run
/// the same script twice.
/// </summary>
public sealed class LiveLogMergeProcessor : IDisposable
{
    private readonly LogMergeService _engine = new();
    private readonly Dictionary<(Guid ServiceId, Guid StepId), StreamState> _states = new();
    private readonly Dictionary<string, bool> _notificationUsage = new(StringComparer.Ordinal);
    private bool _disposed;

    public IReadOnlyList<string> Process(ServiceConfig service, LogEntry entry)
    {
        if (!entry.StepId.HasValue)
            return Array.Empty<string>();

        var key = (service.Id, entry.StepId.Value);
        var step = service.ScriptSteps.FirstOrDefault(candidate => candidate.Id == entry.StepId.Value);
        var script = step?.LogMergeScript;
        if (string.IsNullOrWhiteSpace(script) || !UsesNotificationFunction(script))
        {
            _states.Remove(key);
            return Array.Empty<string>();
        }

        if (!_states.TryGetValue(key, out var state) ||
            !string.Equals(state.Script, script, StringComparison.Ordinal))
        {
            state = new StreamState(script);
            _states[key] = state;
        }

        var currentLine = FormatLogLine(entry);
        var globals = new MergeScriptGlobals
        {
            PreviousLine = state.LastLine,
            CurrentLine = currentLine,
            PreviousResult = state.LastResult,
            PreviousWasCollapsed = state.LastResult?.Collapse == true && state.InCollapseGroup,
            InCollapseGroup = state.InCollapseGroup
        };

        var result = _engine.Evaluate(script, globals);
        entry.HasPrecomputedMergeResult = true;
        entry.PrecomputedMergeScript = script;
        entry.PrecomputedMergeResult = result;

        state.LastLine = currentLine;
        state.LastResult = result;
        // The log window treats any non-null result that cannot collapse into an existing group as a
        // new group header. A null result closes the current group.
        state.InCollapseGroup = result != null;

        return globals.Notifications;
    }

    public void Clear(Guid serviceId, Guid? stepId = null)
    {
        if (stepId.HasValue)
        {
            _states.Remove((serviceId, stepId.Value));
            return;
        }

        foreach (var key in _states.Keys.Where(key => key.ServiceId == serviceId).ToList())
            _states.Remove(key);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _states.Clear();
        _notificationUsage.Clear();
        _engine.Dispose();
    }

    private bool UsesNotificationFunction(string script)
    {
        if (_notificationUsage.TryGetValue(script, out var usesNotification))
            return usesNotification;

        // Parse the body inside a synthetic method so comments and string literals containing
        // "Notify(...)" do not activate live background evaluation. Semantic binding is unnecessary:
        // the generated merge wrapper reserves Notify as the host-provided local delegate.
        var source = $"class MergeProbe {{ object? Run() {{ {script} }} }}";
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        usesNotification = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation => invocation.Expression is IdentifierNameSyntax identifier &&
                               identifier.Identifier.ValueText == "Notify");
        _notificationUsage[script] = usesNotification;
        return usesNotification;
    }

    private static string FormatLogLine(LogEntry entry) =>
        $"{entry.Timestamp:HH:mm:ss} [{entry.Level}] {entry.Message}";

    private sealed class StreamState(string script)
    {
        public string Script { get; } = script;
        public string? LastLine { get; set; }
        public MergeResult? LastResult { get; set; }
        public bool InCollapseGroup { get; set; }
    }
}
