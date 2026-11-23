using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using GymApp.App.ViewModels;

namespace GymApp.App.Views;

/// <summary>Brushes for the split usage donut and its legend: tone 0 is the split being viewed (the accent), 1–5 the
/// others' greys (Tone1..Tone5 in GainStreakResources), looked up for the control's current theme.</summary>
internal static class DonutTones
{
    public static IBrush Brush(Control owner, int tone) =>
        owner.TryFindResource(tone == 0 ? "AccentInk" : $"Tone{Math.Clamp(tone, 1, 5)}", owner.ActualThemeVariant, out var r)
        && r is IBrush brush ? brush : Brushes.Gray;
}

/// <summary>
/// A ring chart of <see cref="Slices"/>, starting at 12 o'clock and going clockwise, with a small gap between slices.
/// The split being viewed (<see cref="SplitUsageSlice.IsThis"/>) is drawn a little thicker. Square: it uses the
/// smaller of its width and height. Put the center label over it in a Panel.
/// </summary>
public class Donut : Control
{
    // The design canvas's ring: radius 15.9 and strokes 5 (6 for this split) in a 42-unit box, 1% gap per slice
    private const double ViewBox = 42, Radius = 15.9155, Stroke = 5, ThisStroke = 6, GapPercent = 1;

    public static readonly StyledProperty<IReadOnlyList<SplitUsageSlice>?> SlicesProperty =
        AvaloniaProperty.Register<Donut, IReadOnlyList<SplitUsageSlice>?>(nameof(Slices));

    static Donut() => AffectsRender<Donut>(SlicesProperty);

    public IReadOnlyList<SplitUsageSlice>? Slices { get => GetValue(SlicesProperty); set => SetValue(SlicesProperty, value); }

    public Donut() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();   // the tones change with the theme

    public override void Render(DrawingContext context)
    {
        if (Slices is not { Count: > 0 } slices) return;
        var size = Math.Min(Bounds.Width, Bounds.Height);
        var scale = size / ViewBox;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = Radius * scale;

        var at = 0.0;
        foreach (var s in slices)
        {
            var pen = new Pen(DonutTones.Brush(this, s.Tone), (s.IsThis ? ThisStroke : Stroke) * scale);
            var sweep = Math.Max(s.Percent - GapPercent, 0.5);
            if (s.Percent >= 100) context.DrawEllipse(null, pen, center, radius, radius);
            else if (s.Percent > 0) context.DrawGeometry(null, pen, Arc(center, radius, at, sweep));
            at += s.Percent;
        }
    }

    /// <summary>The arc from <paramref name="startPercent"/> around the ring, 0 being 12 o'clock.</summary>
    private static StreamGeometry Arc(Point center, double radius, double startPercent, double sweepPercent)
    {
        static Point On(Point c, double r, double percent)
        {
            var angle = percent / 100 * 2 * Math.PI - Math.PI / 2;
            return new Point(c.X + r * Math.Cos(angle), c.Y + r * Math.Sin(angle));
        }

        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        g.BeginFigure(On(center, radius, startPercent), false);
        g.ArcTo(On(center, radius, startPercent + sweepPercent), new Size(radius, radius), 0,
            isLargeArc: sweepPercent > 50, SweepDirection.Clockwise);
        g.EndFigure(false);
        return geometry;
    }
}

/// <summary>A legend square in a donut tone; follows the theme like the donut.</summary>
public class DonutSwatch : Control
{
    public static readonly StyledProperty<int> ToneProperty = AvaloniaProperty.Register<DonutSwatch, int>(nameof(Tone));

    static DonutSwatch()
    {
        AffectsRender<DonutSwatch>(ToneProperty);
        WidthProperty.OverrideDefaultValue<DonutSwatch>(14);
        HeightProperty.OverrideDefaultValue<DonutSwatch>(14);
    }

    public int Tone { get => GetValue(ToneProperty); set => SetValue(ToneProperty, value); }

    public DonutSwatch() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    public override void Render(DrawingContext context) =>
        context.DrawRectangle(DonutTones.Brush(this, Tone), null, new Rect(Bounds.Size), 4, 4);
}
