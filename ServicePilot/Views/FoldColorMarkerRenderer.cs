using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Rendering;

namespace ServicePilot.Views;

/// <summary>
/// Draws a solid color BLOCK in a reserved gap between the fold's "+" marker and the summary text of each
/// COLLAPSED fold. AvalonEdit's own fold placeholder text can only be one global color per editor, so it
/// cannot show "this fold red (has errors), that fold normal". This overlay renderer works around that
/// without touching AvalonEdit's folding pipeline: the log window pads each fold's Title with leading
/// spaces (see <see cref="BlockWidth"/>) to push the text right, and this renderer fills that gap with the
/// fold's first-line content color. The block is large and clearly visible, and never overlaps the text.
/// Colors are supplied per fold start offset via <see cref="Colors"/>.
/// </summary>
public sealed class FoldColorMarkerRenderer : IBackgroundRenderer
{
    /// <summary>Width of the reserved color-block gap, in pixels. The log window reserves this via spaces.</summary>
    public const double BlockWidth = 100.0;

    // Left inset leaves room for the collapsed "+/-" marker box that AvalonEdit draws at the line start.
    private const double BlockLeft = 4.0;
    private const double BlockVerticalPadding = 1.0;

    private readonly FoldingManager _foldingManager;

    /// <summary>Maps a fold's document start offset to the brush of its folded content (first line).</summary>
    public Dictionary<int, System.Windows.Media.Brush> Colors { get; } = new();

    public FoldColorMarkerRenderer(FoldingManager foldingManager)
    {
        _foldingManager = foldingManager;
    }

    // Draw on the selection layer so the block sits on top of the line background but under the text.
    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Colors.Count == 0 || !textView.VisualLinesValid)
            return;

        foreach (var visualLine in textView.VisualLines)
        {
            var lineStartOffset = visualLine.FirstDocumentLine.Offset;
            // Only the header line of a folded section carries the placeholder text; match by start offset.
            if (!Colors.TryGetValue(lineStartOffset, out var brush))
                continue;
            if (!IsFoldedHeader(lineStartOffset))
                continue;

            var lineTop = visualLine.VisualTop - textView.VerticalOffset;
            var y = lineTop + BlockVerticalPadding;
            var h = Math.Max(1, visualLine.Height - BlockVerticalPadding * 2);
            var rect = new Rect(BlockLeft, y, BlockWidth, h);
            drawingContext.DrawRectangle(brush, null, rect);
        }
    }

    private bool IsFoldedHeader(int startOffset)
    {
        foreach (var section in _foldingManager.AllFoldings)
        {
            if (section.IsFolded && section.StartOffset == startOffset)
                return true;
        }
        return false;
    }
}
