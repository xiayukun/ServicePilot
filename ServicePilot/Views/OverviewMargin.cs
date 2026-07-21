using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Folding;
using ServicePilot.Models;

namespace ServicePilot.Views;

/// <summary>
/// Right-side color overview map (next to the native scrollbar). It paints one row per PIXEL (not per
/// log entry), and each pixel row takes the HIGHEST-priority color among the entries it covers
/// (Error > Warning > custom merge color > System > normal). This guarantees a single red error line
/// stays visible even inside a huge run of gray folded progress lines — it is never averaged away.
/// Clicking anywhere scrolls the editor to the corresponding line. It intentionally does NOT draw a
/// viewport thumb or handle dragging: scrolling is left to the editor's native scrollbar, so there is
/// no per-scroll repaint and therefore no lag.
/// </summary>
public sealed class OverviewMargin : FrameworkElement
{
    private const double BarWidth = 12.0;

    private readonly IReadOnlyList<LogEntry> _entries;
    private readonly TextEditor _editor;
    private readonly FoldingManager _foldingManager;
    private DrawingGroup? _cache;
    private List<int> _visibleIndices = new();
    private (int Entries, int Folded, double Height) _lastSignature = (-1, -1, -1);

    public OverviewMargin(IReadOnlyList<LogEntry> entries, TextEditor editor, FoldingManager foldingManager)
    {
        _entries = entries;
        _editor = editor;
        _foldingManager = foldingManager;
        Width = BarWidth;
        Cursor = System.Windows.Input.Cursors.Hand;
        SizeChanged += (_, _) => InvalidateVisualCache();
    }

    /// <summary>
    /// Builds the list of currently VISIBLE entry indices: entries hidden inside a folded section are
    /// skipped, so a folded group of thousands of gray progress lines occupies just its one header row.
    /// This keeps an error line below a big folded block near its real on-screen position instead of
    /// being squashed into a sub-pixel sliver at the very bottom.
    /// </summary>
    private List<int> BuildVisibleIndices()
    {
        var doc = _editor.Document;
        var visible = new List<int>(_entries.Count);
        for (var i = 0; i < _entries.Count && i < doc.LineCount; i++)
        {
            var line = doc.GetLineByNumber(i + 1);
            // A line is hidden when it is strictly inside a folded section (after the header line).
            var folded = false;
            foreach (var section in _foldingManager.GetFoldingsContaining(line.Offset))
            {
                if (section.IsFolded && line.Offset > section.StartOffset)
                {
                    folded = true;
                    break;
                }
            }
            if (!folded)
                visible.Add(i);
        }
        return visible;
    }

    protected override Size MeasureOverride(Size availableSize) => new(BarWidth, 0);

    /// <summary>
    /// Requests a color-map rebuild, but only when something that affects the map actually changed
    /// (entry count, folded-section count, or height). Pure scrolling raises VisualLinesChanged too, so
    /// this cheap signature guard avoids rebuilding the O(n) map on every scroll frame (that was a lag
    /// source). Call directly after content or fold changes.
    /// </summary>
    public void InvalidateVisualCache()
    {
        var signature = (_entries.Count, CountFolded(), Math.Floor(ActualHeight));
        if (signature == _lastSignature)
            return;
        _lastSignature = signature;
        _cache = null;
        InvalidateVisual();
    }

    private int CountFolded()
    {
        var n = 0;
        foreach (var section in _foldingManager.AllFoldings)
            if (section.IsFolded)
                n++;
        return n;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var height = ActualHeight;
        if (height <= 0)
            return;

        var track = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
        track.Freeze();
        drawingContext.DrawRectangle(track, null, new Rect(0, 0, BarWidth, height));

        if (_entries.Count == 0)
            return;

        _cache ??= BuildCache(height);
        drawingContext.DrawDrawing(_cache);
    }

    private DrawingGroup BuildCache(double height)
    {
        var rows = Math.Max(1, (int)Math.Floor(height));
        _visibleIndices = BuildVisibleIndices();
        var count = _visibleIndices.Count;

        var dg = new DrawingGroup();
        if (count == 0)
        {
            dg.Freeze();
            return dg;
        }

        using (var dc = dg.Open())
        {
            // Aggregate the VISIBLE entries into pixel rows, keeping the highest-priority color per row
            // (Error > Warning > custom > System > normal) so red/yellow are never averaged away.
            for (var row = 0; row < rows; row++)
            {
                var startIdx = (int)((double)row / rows * count);
                var endIdx = (int)((double)(row + 1) / rows * count);
                if (endIdx <= startIdx)
                    endIdx = startIdx + 1;
                endIdx = Math.Min(endIdx, count);

                var best = -1;
                Color bestColor = default;
                for (var v = startIdx; v < endIdx; v++)
                {
                    var (priority, color) = ClassifyEntry(_entries[_visibleIndices[v]]);
                    if (priority > best)
                    {
                        best = priority;
                        bestColor = color;
                    }
                }
                if (best < 0)
                    continue;

                var brush = new SolidColorBrush(bestColor);
                brush.Freeze();
                dc.DrawRectangle(brush, null, new Rect(2, row, BarWidth - 4, 1));
            }
        }
        dg.Freeze();
        return dg;
    }

    /// <summary>Returns (priority, color). Higher priority wins when many entries share one pixel row.</summary>
    private static (int Priority, Color Color) ClassifyEntry(LogEntry entry)
    {
        if (entry.Level == LogLevel.Error)
            return (4, Color.FromRgb(0xF4, 0x43, 0x36));
        if (entry.Level == LogLevel.Warning)
            return (3, Color.FromRgb(0xFF, 0xD5, 0x4F));

        var merge = ParseColor(entry.MergeColor);
        if (merge.HasValue)
            return (2, merge.Value);

        if (entry.Level == LogLevel.System)
            return (1, Color.FromRgb(0x88, 0x88, 0x88));

        return (0, Color.FromRgb(0x55, 0x55, 0x66));
    }

    private static Color? ParseColor(string? colorText)
    {
        if (string.IsNullOrWhiteSpace(colorText))
            return null;
        try
        {
            var brush = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(colorText)!;
            if (brush is SolidColorBrush scb)
                return scb.Color;
        }
        catch
        {
            // Ignore invalid colors.
        }
        return null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_entries.Count == 0 || ActualHeight <= 0)
            return;

        var y = e.GetPosition(this).Y;
        // Map the click through the visible-entry list so it lands on the right on-screen position.
        if (_visibleIndices.Count > 0)
        {
            var v = (int)(y / ActualHeight * _visibleIndices.Count);
            v = Math.Clamp(v, 0, _visibleIndices.Count - 1);
            _editor.ScrollToLine(_visibleIndices[v] + 1);
        }
        else
        {
            var lineIndex = Math.Clamp((int)(y / ActualHeight * _entries.Count), 0, _entries.Count - 1);
            _editor.ScrollToLine(lineIndex + 1);
        }
        e.Handled = true;
    }
}
