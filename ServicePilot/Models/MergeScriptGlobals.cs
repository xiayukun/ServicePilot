namespace ServicePilot.Models;

/// <summary>
/// Globals object injected into Roslyn Scripting merge functions.
/// Provides context from the log stream for each evaluation.
/// </summary>
public class MergeScriptGlobals
{
    private const int MaxNotificationsPerLine = 4;
    private const int MaxNotificationLength = 1000;
    private readonly object _notificationGate = new();
    private readonly List<string> _notifications = new();

    /// <summary>
    /// The log line immediately before the current line (null if this is the first line).
    /// </summary>
    public string? PreviousLine { get; set; }

    /// <summary>
    /// The current log line being evaluated for merging.
    /// </summary>
    public string? CurrentLine { get; set; }

    /// <summary>
    /// The <see cref="MergeResult"/> returned by the script for the previous line, or null if the previous
    /// line produced no result (or this is the first line). Lets a script carry state forward via
    /// <see cref="MergeResult.State"/> and inspect the previous line's MergedMessage/Color/Collapse.
    /// This is runtime-only state: it is NOT persisted and is NOT restored on tab rebuild.
    /// </summary>
    public MergeResult? PreviousResult { get; set; }

    /// <summary>
    /// True when the previous line was folded (a collapsed child of the current group). Convenience flag
    /// equivalent to <c>PreviousResult?.Collapse == true</c> combined with an open group.
    /// </summary>
    public bool PreviousWasCollapsed { get; set; }

    /// <summary>
    /// True when there is already an open collapse group the current line could fold into (i.e. a previous
    /// line returned Collapse=false and became the group header). When false, returning Collapse=true has
    /// no effect and the line becomes a new group header instead.
    /// </summary>
    public bool InCollapseGroup { get; set; }

    /// <summary>
    /// Notifications requested by the script while evaluating this line. The host decides whether to
    /// display them; validation, CLI previews, and historical log replay never produce system popups.
    /// </summary>
    public IReadOnlyList<string> Notifications
    {
        get
        {
            lock (_notificationGate)
                return _notifications.ToArray();
        }
    }

    /// <summary>
    /// Records a best-effort Windows notification request. Generated scripts receive this method as the
    /// local <c>Notify(string message)</c> delegate. Empty messages are ignored and per-line requests are
    /// bounded so an accidental loop cannot grow host memory without limit.
    /// </summary>
    public void RequestNotification(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var normalized = message.Trim();
        if (normalized.Length > MaxNotificationLength)
            normalized = normalized[..MaxNotificationLength];

        lock (_notificationGate)
        {
            if (_notifications.Count < MaxNotificationsPerLine)
                _notifications.Add(normalized);
        }
    }
}
