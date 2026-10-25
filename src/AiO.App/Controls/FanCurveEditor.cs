using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AiO.Core.Asus;

namespace AiO.App.Controls;

/// <summary>Interactive 8-point fan curve: drag points to change temperature (x) and duty (y).</summary>
public sealed class FanCurveEditor : FrameworkElement
{
    private const double MinTemp = 20, MaxTemp = 110;
    private const double PadLeft = 40, PadBottom = 26, PadTop = 10, PadRight = 12;
    private const double PointRadius = 6;

    public static readonly DependencyProperty CurveProperty =
        DependencyProperty.Register(nameof(Curve), typeof(FanCurve), typeof(FanCurveEditor),
            // One-way: the editor changes the FanCurve object in place, so there's nothing to write back.
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public FanCurve? Curve { get => (FanCurve?)GetValue(CurveProperty); set => SetValue(CurveProperty, value); }

    /// <summary>Raised after the user finishes dragging a point.</summary>
    public event EventHandler? CurveEdited;

    private int _dragIndex = -1;

    private readonly Pen _gridPen;
    private readonly Pen _linePen;
    private readonly Brush _pointBrush;
    private readonly Brush _textBrush;
    private readonly Typeface _typeface = new("Segoe UI");

    public FanCurveEditor()
    {
        MinHeight = 220;
        Cursor = Cursors.Cross;
        _gridPen = new Pen((Brush)Application.Current.FindResource("BorderBrush"), 1);
        _linePen = new Pen((Brush)Application.Current.FindResource("AccentBrush"), 2.5);
        _pointBrush = Brushes.White;
        _textBrush = (Brush)Application.Current.FindResource("TextMutedBrush");
        _gridPen.Freeze();
        _linePen.Freeze();
    }

    private Rect Plot => new(PadLeft, PadTop, Math.Max(1, ActualWidth - PadLeft - PadRight), Math.Max(1, ActualHeight - PadTop - PadBottom));

    private Point ToScreen(double temp, double duty)
    {
        var p = Plot;
        return new Point(p.Left + (temp - MinTemp) / (MaxTemp - MinTemp) * p.Width, p.Bottom - duty / 100 * p.Height);
    }

    private (double Temp, double Duty) FromScreen(Point point)
    {
        var p = Plot;
        double temp = MinTemp + (point.X - p.Left) / p.Width * (MaxTemp - MinTemp);
        double duty = (p.Bottom - point.Y) / p.Height * 100;
        return (Math.Clamp(temp, MinTemp, MaxTemp), Math.Clamp(duty, 0, 100));
    }

    private FormattedText Text(string s) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, _typeface, 11, _textBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize)); // hit-testable background
        var plot = Plot;

        for (int duty = 0; duty <= 100; duty += 20)
        {
            var y = ToScreen(MinTemp, duty).Y;
            dc.DrawLine(_gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Text($"{duty}%");
            dc.DrawText(label, new Point(plot.Left - label.Width - 6, y - label.Height / 2));
        }
        for (int temp = 20; temp <= 110; temp += 10)
        {
            var x = ToScreen(temp, 0).X;
            dc.DrawLine(_gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            var label = Text($"{temp}°");
            dc.DrawText(label, new Point(x - label.Width / 2, plot.Bottom + 6));
        }

        if (Curve is not { } curve) return;
        var points = Enumerable.Range(0, FanCurve.PointCount).Select(i => ToScreen(curve.Temperatures[i], curve.Duties[i])).ToArray();

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            ctx.PolyLineTo(points[1..], true, true);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, _linePen, geometry);

        for (int i = 0; i < points.Length; i++)
        {
            dc.DrawEllipse(_pointBrush, _linePen, points[i], PointRadius, PointRadius);
            if (i == _dragIndex)
            {
                var tip = Text($"{curve.Temperatures[i]}°C · {curve.Duties[i]}%");
                dc.DrawText(tip, new Point(points[i].X - tip.Width / 2, points[i].Y - tip.Height - 10));
            }
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (Curve is not { } curve) return;
        var pos = e.GetPosition(this);
        double best = double.MaxValue;
        for (int i = 0; i < FanCurve.PointCount; i++)
        {
            double d = (ToScreen(curve.Temperatures[i], curve.Duties[i]) - pos).Length;
            if (d < best && d < 16) { best = d; _dragIndex = i; }
        }
        if (_dragIndex >= 0)
        {
            CaptureMouse();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragIndex < 0 || Curve is not { } curve) return;
        var (temp, duty) = FromScreen(e.GetPosition(this));
        curve.Temperatures[_dragIndex] = (byte)Math.Round(temp);
        curve.Duties[_dragIndex] = (byte)Math.Round(duty);
        curve.Normalize();
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_dragIndex < 0) return;
        _dragIndex = -1;
        ReleaseMouseCapture();
        InvalidateVisual();
        CurveEdited?.Invoke(this, EventArgs.Empty);
    }
}
