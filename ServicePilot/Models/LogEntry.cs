namespace ServicePilot.Models;

public enum LogLevel
{
    Info,
    Warning,
    Error,
    System
}

public class LogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public LogLevel Level { get; set; } = LogLevel.Info;
    public string Message { get; set; } = string.Empty;
    public string Source { get; set; } = "stdout";
    public string? StepName { get; set; }
    public Guid? StepId { get; set; }

    // --- Transient log-merge/folding metadata (UI only, never persisted) ---

    /// <summary>
    /// When this entry is the header of a collapse group, holds the summary text produced by the merge
    /// script (MergedMessage of the last folded child). Shown on the folded header line and as the fold
    /// title. Null when the entry is not a group header.
    /// </summary>
    public string? GroupSummary { get; set; }

    /// <summary>
    /// True when the merge script asked to collapse this entry into the previous line. Such entries are
    /// folded away by default and only visible when the user expands the group with the left-side toggle.
    /// </summary>
    public bool IsCollapsedChild { get; set; }

    /// <summary>Custom foreground color (from MergeResult.Color) for this line, if any.</summary>
    public string? MergeColor { get; set; }

    // A live notification-capable merge script is evaluated once at application level so it can notify
    // even when no log window is open. The log window reuses this transient result instead of running
    // the same script (and its side effects) a second time. Internal properties are excluded from CLI JSON.
    internal bool HasPrecomputedMergeResult { get; set; }
    internal string? PrecomputedMergeScript { get; set; }
    internal MergeResult? PrecomputedMergeResult { get; set; }

    public LogEntry() { }

    public LogEntry(LogLevel level, string message, string source, string? stepName = null, Guid? stepId = null)
    {
        Timestamp = DateTime.Now;
        Level = level;
        Message = message;
        Source = source;
        StepName = stepName;
        StepId = stepId;
    }
}
