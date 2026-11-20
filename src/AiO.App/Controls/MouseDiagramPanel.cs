using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AiO.App.Controls;

/// <summary>
/// Lays its children out on both sides of a top-down mouse drawing and draws a line from each child
/// to the part of the mouse it controls. Each child says where with the attached <c>Side</c>
/// (0 left column, 1 right column) and <c>AnchorX</c>/<c>AnchorY</c> (0-1 within the drawing).
/// </summary>
public sealed class MouseDiagramPanel : Panel
{
    private const double DrawingWidth = 150;
    private const double DrawingHeight = 260;
    private const double Gap = 36;
    private const double RowSpacing = 6;

    public static readonly DependencyProperty SideProperty = DependencyProperty.RegisterAttached(
        "Side", typeof(int), typeof(MouseDiagramPanel), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsParentArrange));
    public static readonly DependencyProperty AnchorXProperty = DependencyProperty.RegisterAttached(
        "AnchorX", typeof(double), typeof(MouseDiagramPanel), new FrameworkPropertyMetadata(0.5, FrameworkPropertyMetadataOptions.AffectsParentArrange));
    public static readonly DependencyProperty AnchorYProperty = DependencyProperty.RegisterAttached(
        "AnchorY", typeof(double), typeof(MouseDiagramPanel), new FrameworkPropertyMetadata(0.5, FrameworkPropertyMetadataOptions.AffectsParentArrange));
    public static readonly DependencyProperty HighlightProperty = DependencyProperty.RegisterAttached(
        "Highlight", typeof(bool), typeof(MouseDiagramPanel), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static int GetSide(DependencyObject o) => (int)o.GetValue(SideProperty);
    public static void SetSide(DependencyObject o, int v) => o.SetValue(SideProperty, v);
    public static double GetAnchorX(DependencyObject o) => (double)o.GetValue(AnchorXProperty);
    public static void SetAnchorX(DependencyObject o, double v) => o.SetValue(AnchorXProperty, v);
    public static double GetAnchorY(DependencyObject o) => (double)o.GetValue(AnchorYProperty);
    public static void SetAnchorY(DependencyObject o, double v) => o.SetValue(AnchorYProperty, v);
    public static bool GetHighlight(DependencyObject o) => (bool)o.GetValue(HighlightProperty);
    public static void SetHighlight(DependencyObject o, bool v) => o.SetValue(HighlightProperty, v);

    private double _columnWidth;
    private double _drawingTop;
    private readonly List<(Point From, Point To, bool Highlight)> _lines = [];

    private double DrawingLeft => _columnWidth + Gap;

    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? 900 : available.Width;
        _columnWidth = Math.Max(180, (width - DrawingWidth - 2 * Gap) / 2);
        double left = 0, right = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(_columnWidth, double.PositiveInfinity));
            if (GetSide(child) == 0) left += child.DesiredSize.Height + RowSpacing;
            else right += child.DesiredSize.Height + RowSpacing;
        }
        double height = Math.Max(DrawingHeight + 20, Math.Max(left, right));
        return new Size(_columnWidth * 2 + DrawingWidth + 2 * Gap, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _lines.Clear();
        double maxColumn = 0;
        foreach (var side in new[] { 0, 1 })
        {
            double total = 0;
            foreach (UIElement c in InternalChildren) if (GetSide(c) == side) total += c.DesiredSize.Height + RowSpacing;
            maxColumn = Math.Max(maxColumn, total);
        }
        _drawingTop = Math.Max(10, (Math.Max(finalSize.Height, maxColumn) - DrawingHeight) / 2);

        foreach (var side in new[] { 0, 1 })
        {
            var rows = InternalChildren.Cast<UIElement>().Where(c => GetSide(c) == side)
                .OrderBy(GetAnchorY).ToList();
            // Place each row level with its anchor where there's room, pushing later rows down.
            double y = 0;
            double total = rows.Sum(r => r.DesiredSize.Height + RowSpacing);
            double slack = Math.Max(0, finalSize.Height - total);
            foreach (var row in rows)
            {
                double h = row.DesiredSize.Height;
                double anchorY = _drawingTop + GetAnchorY(row) * DrawingHeight;
                double wanted = anchorY - h / 2;
                double used = rows.TakeWhile(r => r != row).Sum(r => r.DesiredSize.Height + RowSpacing);
                double latest = used + slack; // rows below still need their space
                y = Math.Clamp(wanted, y, Math.Max(y, latest));
                double x = side == 0 ? 0 : DrawingLeft + DrawingWidth + Gap;
                row.Arrange(new Rect(x, y, _columnWidth, h));

                var anchor = new Point(DrawingLeft + GetAnchorX(row) * DrawingWidth, anchorY);
                var from = new Point(side == 0 ? _columnWidth + 4 : x - 4, y + Math.Min(h, 32) / 2);
                _lines.Add((from, anchor, GetHighlight(row)));
                y += h + RowSpacing;
            }
        }
        InvalidateVisual();
        return finalSize;
    }

    private Brush Res(string key, Color fallback) => TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

    protected override void OnRender(DrawingContext dc)
    {
        var outline = new Pen(Res("TextMutedBrush", Colors.Gray), 1.5);
        var fill = Res("SurfaceAltBrush", Color.FromRgb(0x27, 0x2A, 0x31));
        var line = new Pen(Res("TextMutedBrush", Colors.Gray), 1) { DashStyle = DashStyles.Solid };
        var accent = Res("AccentBrush", Colors.DodgerBlue);
        var accentPen = new Pen(accent, 1.5);

        double l = DrawingLeft, t = _drawingTop, w = DrawingWidth, h = DrawingHeight;
        Point P(double x, double y) => new(l + x * w, t + y * h);

        // Body: rounded front, wider grip at the back, thumb rest bulge on the left.
        var body = new StreamGeometry();
        using (var g = body.Open())
        {
            g.BeginFigure(P(0.5, 0.0), true, true);
            g.BezierTo(P(0.85, 0.0), P(0.95, 0.12), P(0.95, 0.35), true, true);
            g.BezierTo(P(0.97, 0.6), P(1.0, 0.85), P(0.75, 0.98), true, true);
            g.BezierTo(P(0.6, 1.02), P(0.4, 1.02), P(0.25, 0.98), true, true);
            g.BezierTo(P(0.0, 0.88), P(-0.02, 0.62), P(0.05, 0.45), true, true);
            g.BezierTo(P(0.08, 0.3), P(0.05, 0.12), P(0.5, 0.0), true, true);
        }
        body.Freeze();
        dc.DrawGeometry(fill, outline, body);

        // Split between left and right buttons, and the button edge.
        dc.DrawLine(outline, P(0.5, 0.0), P(0.5, 0.12));
        dc.DrawLine(outline, P(0.5, 0.32), P(0.5, 0.45));
        var edge = new StreamGeometry();
        using (var g = edge.Open())
        {
            g.BeginFigure(P(0.07, 0.42), false, false);
            g.BezierTo(P(0.3, 0.47), P(0.7, 0.47), P(0.94, 0.42), true, true);
        }
        edge.Freeze();
        dc.DrawGeometry(null, outline, edge);

        // Wheel and DPI buttons behind it.
        dc.DrawRoundedRectangle(Res("SurfaceBrush", Colors.Black), outline, new Rect(P(0.44, 0.12), P(0.56, 0.31)), 6, 6);
        for (double y = 0.15; y < 0.29; y += 0.03) dc.DrawLine(line, P(0.46, y), P(0.54, y));
        dc.DrawRoundedRectangle(null, outline, new Rect(P(0.46, 0.34), P(0.54, 0.38)), 2, 2);
        dc.DrawRoundedRectangle(null, outline, new Rect(P(0.46, 0.40), P(0.54, 0.44)), 2, 2);

        // Side buttons on the left flank.
        dc.DrawRoundedRectangle(null, outline, new Rect(P(0.02, 0.36), P(0.08, 0.44)), 3, 3);
        dc.DrawRoundedRectangle(null, outline, new Rect(P(0.02, 0.46), P(0.08, 0.54)), 3, 3);

        foreach (var (from, to, highlight) in _lines)
        {
            var pen = highlight ? accentPen : line;
            dc.DrawLine(pen, from, to);
            dc.DrawEllipse(highlight ? accent : pen.Brush, null, to, 3, 3);
        }
    }
}
