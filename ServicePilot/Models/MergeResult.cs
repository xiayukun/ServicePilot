namespace ServicePilot.Models;

/// <summary>
/// Result of a log merge script evaluation. Returned by Roslyn scripting merge functions.
/// </summary>
public class MergeResult
{
    /// <summary>
    /// Summary text for the collapse group. On a group header (Collapse=false) it is the text shown on
    /// the folded one-line view; on a collapsed line (Collapse=true) it refreshes the header's live
    /// summary (e.g. "compiling 67%"). Raw lines are always kept, so the group can be re-expanded.
    /// When null, the line's original text is displayed.
    /// </summary>
    public string? MergedMessage { get; set; }

    /// <summary>
    /// Optional color hint for the merged line. Accepts any WPF-parseable color: named colors
    /// (e.g. "Gray", "Yellow", "OrangeRed") or hex ("#FF8800"). Invalid values are ignored.
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// When true, this line is folded into the group started by the previous non-collapsed line. The
    /// group becomes a real expandable fold: it shows a single summary line with a left-side ">" toggle,
    /// and every raw line stays available when expanded (toggle, "Summary" button, or a search hit).
    /// The FIRST line of a group must return Collapse=false (it becomes the header/anchor); subsequent
    /// lines return Collapse=true. A collapse only takes effect when the previous entry was itself
    /// produced by the merge script.
    /// </summary>
    public bool Collapse { get; set; }

    /// <summary>
    /// Reserved for future hierarchical (tree-style) merge display. Not yet rendered by the log
    /// window; safe to leave null. Exposed by "merge-script test --json" for forward compatibility.
    /// </summary>
    public List<MergeResult>? Children { get; set; }

    /// <summary>
    /// Optional cross-line carry state. Whatever the script stores here is handed to the NEXT line's
    /// evaluation as <c>PreviousResult.State</c>, so a script can accumulate counters, remember the last
    /// progress value, detect runs of similar lines, etc.
    ///
    /// Constraints (important):
    /// - Runtime only: never persisted to config, and NOT restored when a tab is rebuilt (tab switch /
    ///   clear replays folds from stored flags without re-running the script). Do not rely on old state
    ///   surviving a rebuild.
    /// - Store simple values only (string / int / double / bool). The script runs in a collectible
    ///   AssemblyLoadContext; storing script-defined types can leak references and block unload.
    /// - Per action tab: state never crosses tabs.
    /// </summary>
    public Dictionary<string, object?>? State { get; set; }
}
