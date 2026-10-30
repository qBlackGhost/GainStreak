using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using GymApp.App.ViewModels;

namespace GymApp.App.Views;

/// <summary>
/// The Profile's weight trend: a line through the weigh-ins, oldest to newest, with a dot on each. Tapping (or
/// clicking) near a dot selects it and shows its weight and date in a bubble above it, like the Health app's weight
/// chart; the newest is selected to start with. The line stays crisp at any width (no stretching, so the dots stay
/// round).
/// </summary>
public class WeightChart : Control
{
    private const double Dot = 4, SelectedDot = 6, PadX = 14, Bubble = 50, PadBottom = 10;

    public static readonly StyledProperty<IReadOnlyList<WeightChartPoint>?> PointsProperty =
        AvaloniaProperty.Register<WeightChart, IReadOnlyList<WeightChartPoint>?>(nameof(Points));

    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<WeightChart, int>(nameof(SelectedIndex), -1);

    static WeightChart()
    {
        AffectsRender<WeightChart>(PointsProperty, SelectedIndexProperty);
        FocusableProperty.OverrideDefaultValue<WeightChart>(true);
    }

    public IReadOnlyList<WeightChartPoint>? Points { get => GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    /// <summary>The dot with the bubble; -1 for none.</summary>
    public int SelectedIndex { get => GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }

    public WeightChart()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();   // the brushes change with the theme
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PointsProperty) SelectedIndex = (Points?.Count ?? 0) - 1;   // new data: the newest
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Nearest(e.GetPosition(this).X) is { } i) SelectedIndex = i;
    }

    // The arrow keys move the bubble between weigh-ins
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var n = Points?.Count ?? 0;
        if (n == 0) return;
        if (e.Key == Key.Left) { SelectedIndex = Math.Max(0, SelectedIndex - 1); e.Handled = true; }
        else if (e.Key == Key.Right) { SelectedIndex = Math.Min(n - 1, SelectedIndex + 1); e.Handled = true; }
    }

    private int? Nearest(double x)
    {
        var pts = Layout();
        if (pts.Count == 0) return null;
        var best = 0;
        for (var i = 1; i < pts.Count; i++)
            if (Math.Abs(pts[i].X - x) < Math.Abs(pts[best].X - x)) best = i;
        return best;
    }

    /// <summary>Where each weigh-in sits: spread evenly across, scaled between the lightest and heaviest with half a
    /// unit of headroom, under the bubble's room at the top.</summary>
    private List<Point> Layout()
    {
        if (Points is not { Count: > 0 } points) return [];
        var w = Bounds.Width; var h = Bounds.Height;
        double lo = points.Min(p => p.Value) - 0.5, hi = points.Max(p => p.Value) + 0.5;
        var top = Bubble + SelectedDot; var bottom = h - PadBottom;
        var step = points.Count > 1 ? (w - 2 * PadX) / (points.Count - 1) : 0;
        return points.Select((p, i) => new Point(
            points.Count > 1 ? PadX + i * step : w / 2,
            bottom - (p.Value - lo) / (hi - lo) * (bottom - top))).ToList();
    }

    private IBrush Brush(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var r) && r is IBrush b ? b : fallback;

    public override void Render(DrawingContext context)
    {
        var pts = Layout();
        if (pts.Count == 0) return;

        var accent = Brush("AccentInk", Brushes.YellowGreen);
        var surface = Brush("Surface", Brushes.Black);
        var line = Brush("Line", Brushes.DimGray);
        var ink = Brush("Ink", Brushes.White);
        var muted = Brush("Muted", Brushes.Gray);
        var bubble = Brush("Fill2", Brushes.DarkGray);

        // The baseline, then the line through the weigh-ins
        context.DrawLine(new Pen(line, 1), new Point(0, Bounds.Height - 1), new Point(Bounds.Width, Bounds.Height - 1));
        if (pts.Count > 1)
        {
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(pts[0], false);
                foreach (var p in pts.Skip(1)) g.LineTo(p);
                g.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(accent, 2.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
        }

        // A dot per weigh-in: hollow, the selected one filled and bigger
        var sel = SelectedIndex;
        for (var i = 0; i < pts.Count; i++)
        {
            if (i == sel) continue;
            context.DrawEllipse(surface, new Pen(accent, 2), pts[i], Dot, Dot);
        }
        if (sel < 0 || sel >= pts.Count) return;

        // The bubble: the weight over the date, above the dot and kept inside the chart's edges
        var at = pts[sel];
        var p0 = Points![sel];
        var font = TextElement.GetFontFamily(this);
        var value = Text(p0.ValueText, 13, FontWeight.Bold, ink, font);
        var date = Text(p0.DateText, 11, FontWeight.Normal, muted, font);
        var bw = Math.Max(value.Width, date.Width) + 20;
        var bh = value.Height + date.Height + 8;
        var bx = Math.Clamp(at.X - bw / 2, 0, Math.Max(0, Bounds.Width - bw));
        var box = new Rect(bx, Math.Max(0, at.Y - SelectedDot - 8 - bh), bw, bh);

        // A dashed guide from the bubble down, then the selected dot over it
        context.DrawLine(new Pen(line, 1, new DashStyle([3, 3], 0)), new Point(at.X, box.Bottom), new Point(at.X, Bounds.Height - 1));
        context.DrawEllipse(accent, new Pen(surface, 2), at, SelectedDot, SelectedDot);
        context.DrawRectangle(bubble, null, box, 8, 8);
        context.DrawText(value, new Point(box.X + (bw - value.Width) / 2, box.Y + 4));
        context.DrawText(date, new Point(box.X + (bw - date.Width) / 2, box.Y + 4 + value.Height));
    }

    private static FormattedText Text(string text, double size, FontWeight weight, IBrush brush, FontFamily font) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(font, FontStyle.Normal, weight), size, brush);
}
