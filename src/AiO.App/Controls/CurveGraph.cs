using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace AiO.App.Controls;

/// <summary>Line chart of sensitivity against input speed, with a filled area under the curve.</summary>
public sealed class CurveGraph : FrameworkElement
{
    private const double PadLeft = 44, PadBottom = 28, PadTop = 12, PadRight = 14;

    public static readonly DependencyProperty PointsProperty =
        DependencyProperty.Register(nameof(Points), typeof(IReadOnlyList<(double X, double Y)>), typeof(CurveGraph),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxXProperty =
        DependencyProperty.Register(nameof(MaxX), typeof(double), typeof(CurveGraph),
            new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<(double X, double Y)>? Points
    {
        get => (IReadOnlyList<(double X, double Y)>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public double MaxX { get => (double)GetValue(MaxXProperty); set => SetValue(MaxXProperty, value); }

    /// <summary>Live input speed to mark on the curve (NaN = hide the marker).</summary>
    public static readonly DependencyProperty LiveXProperty =
        DependencyProperty.Register(nameof(LiveX), typeof(double), typeof(CurveGraph),
            new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public double LiveX { get => (double)GetValue(LiveXProperty); set => SetValue(LiveXProperty, value); }

    /// <summary>Sensitivity on the curve at <paramref name="x"/> (linear interpolation between samples).</summary>
    public static double YAt(IReadOnlyList<(double X, double Y)> points, double x)
    {
        if (points.Count == 0) return 1;
        if (x <= points[0].X) return points[0].Y;
        for (int i = 1; i < points.Count; i++)
        {
            if (x > points[i].X) continue;
            var (x0, y0) = points[i - 1];
            var (x1, y1) = points[i];
            return y0 + (y1 - y0) * (x - x0) / (x1 - x0);
        }
        return points[^1].Y;
    }

    public string XLabel { get; set; } = "input speed (counts/ms)";
    public string YLabel { get; set; } = "sensitivity";

    private readonly Pen _gridPen, _linePen;
    private readonly Brush _fill, _text;
    private readonly Typeface _typeface = new("Segoe UI");

    public CurveGraph()
    {
        MinHeight = 220;
        _gridPen = new Pen((Brush)Application.Current.FindResource("BorderBrush"), 1);
        var accent = (Color)Application.Current.FindResource("AccentColor");
        _linePen = new Pen(new SolidColorBrush(accent), 2.5) { LineJoin = PenLineJoin.Round };
        _fill = new LinearGradientBrush(Color.FromArgb(90, accent.R, accent.G, accent.B), Color.FromArgb(0, accent.R, accent.G, accent.B), 90);
        _text = (Brush)Application.Current.FindResource("TextMutedBrush");
        _gridPen.Freeze();
        _linePen.Freeze();
        _fill.Freeze();
    }

    private FormattedText Text(string s) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, _typeface, 11, _text, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    protected override void OnRender(DrawingContext dc)
    {
        var plot = new Rect(PadLeft, PadTop, Math.Max(1, ActualWidth - PadLeft - PadRight), Math.Max(1, ActualHeight - PadTop - PadBottom));
        var points = Points ?? [];

        double maxY = points.Count > 0 ? points.Max(p => p.Y) : 1;
        double minY = points.Count > 0 ? points.Min(p => p.Y) : 1;
        // Always show 0 to a little above the peak, in friendly steps.
        double top = NiceCeiling(Math.Max(maxY * 1.1, 1.2));
        double bottom = minY < 0.5 ? 0 : Math.Floor(Math.Min(minY, 1) * 2) / 2 - 0.5;
        if (bottom < 0) bottom = 0;

        Point Map(double x, double y) => new(plot.Left + x / MaxX * plot.Width, plot.Bottom - (y - bottom) / (top - bottom) * plot.Height);

        double yStep = NiceStep((top - bottom) / 5);
        for (double y = bottom; y <= top + 1e-9; y += yStep)
        {
            var p = Map(0, y);
            dc.DrawLine(_gridPen, new Point(plot.Left, p.Y), new Point(plot.Right, p.Y));
            var label = Text(y.ToString("0.##", CultureInfo.CurrentCulture) + "×");
            dc.DrawText(label, new Point(plot.Left - label.Width - 6, p.Y - label.Height / 2));
        }
        double xStep = NiceStep(MaxX / 8);
        for (double x = 0; x <= MaxX + 1e-9; x += xStep)
        {
            var p = Map(x, bottom);
            var label = Text(x.ToString("0", CultureInfo.CurrentCulture));
            dc.DrawText(label, new Point(p.X - label.Width / 2, plot.Bottom + 6));
        }
        var axis = Text(XLabel);
        dc.DrawText(axis, new Point(plot.Right - axis.Width, plot.Bottom + 6 + axis.Height));

        if (points.Count < 2) return;
        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(Map(points[0].X, points[0].Y), false, false);
            ctx.PolyLineTo(points.Skip(1).Select(p => Map(p.X, p.Y)).ToList(), true, true);
        }
        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(Map(points[0].X, bottom), true, true);
            ctx.PolyLineTo(points.Select(p => Map(p.X, p.Y)).Append(Map(points[^1].X, bottom)).ToList(), false, true);
        }
        line.Freeze();
        area.Freeze();
        dc.PushClip(new RectangleGeometry(plot));
        dc.DrawGeometry(_fill, null, area);
        dc.DrawGeometry(null, _linePen, line);
        dc.Pop();

        // Live marker: where the mouse is on the curve right now.
        if (!double.IsNaN(LiveX) && LiveX > 0.05)
        {
            double x = Math.Min(LiveX, MaxX);
            double y = YAt(points, x);
            var at = Map(x, y);
            dc.DrawLine(_livePen, new Point(at.X, plot.Top), new Point(at.X, plot.Bottom));
            dc.DrawEllipse(_liveFill, _liveRing, at, 6, 6);
            var label = Text($"{LiveX:0.0} → {y:0.00}×");
            double lx = Math.Clamp(at.X + 10, plot.Left, plot.Right - label.Width);
            double ly = Math.Max(plot.Top, at.Y - label.Height - 8);
            dc.DrawRectangle(_labelBack, null, new Rect(lx - 4, ly - 2, label.Width + 8, label.Height + 4));
            dc.DrawText(label, new Point(lx, ly));
        }
    }

    private static readonly Brush _liveFill = Brushes.White;
    private static readonly Pen _liveRing = CreatePen(Color.FromRgb(0xF5, 0xB5, 0x44), 3);
    private static readonly Pen _livePen = CreatePen(Color.FromArgb(110, 0xF5, 0xB5, 0x44), 1);
    private static readonly Brush _labelBack = CreateBrush(Color.FromArgb(220, 0x1D, 0x1F, 0x25));

    private static Pen CreatePen(Color c, double width)
    {
        var pen = new Pen(new SolidColorBrush(c), width);
        pen.Freeze();
        return pen;
    }

    private static Brush CreateBrush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static double NiceStep(double raw)
    {
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double n = raw / magnitude;
        return (n <= 1 ? 1 : n <= 2 ? 2 : n <= 2.5 ? 2.5 : n <= 5 ? 5 : 10) * magnitude;
    }

    private static double NiceCeiling(double v) => Math.Ceiling(v / NiceStep(v / 5)) * NiceStep(v / 5);
}
