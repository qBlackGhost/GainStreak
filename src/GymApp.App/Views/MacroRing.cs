using System;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using GymApp.Core;

namespace GymApp.App.Views;

/// <summary>The day's macros as a thin ring of up to three round-ended arcs (protein, carbs, fat, clockwise from 12 o'clock),
/// with the kcal eaten over the calorie goal in the middle (Phase 27, design "C"). Draws itself like <see cref="QrCodeView"/>;
/// the legend rows under it belong to the screen that shows it. Square: it uses the smaller of its width and height. No
/// <see cref="Split"/> draws nothing but the centre. The colours are the MacroProtein, MacroCarbs and MacroFat tokens.</summary>
public class MacroRing : Control
{
    public static readonly StyledProperty<MacroSplit?> SplitProperty = AvaloniaProperty.Register<MacroRing, MacroSplit?>(nameof(Split));
    public static readonly StyledProperty<int> EatenKcalProperty = AvaloniaProperty.Register<MacroRing, int>(nameof(EatenKcal));
    public static readonly StyledProperty<int?> GoalKcalProperty = AvaloniaProperty.Register<MacroRing, int?>(nameof(GoalKcal));

    static MacroRing()
    {
        AffectsRender<MacroRing>(SplitProperty, EatenKcalProperty, GoalKcalProperty);
        SplitProperty.Changed.AddClassHandler<MacroRing>((r, _) => r.UpdateName());
    }

    public MacroSplit? Split { get => GetValue(SplitProperty); set => SetValue(SplitProperty, value); }
    public int EatenKcal { get => GetValue(EatenKcalProperty); set => SetValue(EatenKcalProperty, value); }
    /// <summary>The day's goal; null shows only the eaten kcal.</summary>
    public int? GoalKcal { get => GetValue(GoalKcalProperty); set => SetValue(GoalKcalProperty, value); }

    public MacroRing() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    private void UpdateName() => AutomationProperties.SetName(this, Split?.Describe() ?? "No macros logged");

    protected override Size MeasureOverride(Size availableSize)
    {
        var side = Math.Min(availableSize.Width, availableSize.Height);
        if (double.IsInfinity(side)) side = double.IsInfinity(availableSize.Width) ? availableSize.Height : availableSize.Width;
        if (double.IsInfinity(side)) side = 200;
        return new Size(side, side);
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;
        var scale = size / MacroRingGeometry.Box;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = MacroRingGeometry.Radius * scale;
        var stroke = MacroRingGeometry.Stroke * scale;

        if (Split is { } split)
        {
            var arcs = MacroRingGeometry.Arcs(split.ProteinPercent, split.CarbsPercent, split.FatPercent);
            foreach (var arc in arcs)
            {
                var pen = new Pen(ColorOf(arc.Macro), stroke, lineCap: PenLineCap.Round);
                if (MacroRingGeometry.IsFullCircle(arcs)) context.DrawEllipse(null, pen, center, radius, radius);
                else context.DrawGeometry(null, pen, Arc(center, radius, arc.StartDegrees, arc.SweepDegrees));
            }
        }
        else
            context.DrawEllipse(null, new Pen(Resource("Ghost"), stroke), center, radius, radius);   // nothing to split: an empty track

        DrawCentre(context, center, scale);
    }

    private void DrawCentre(DrawingContext context, Point center, double scale)
    {
        var mono = Application.Current?.TryFindResource("Mono", ActualThemeVariant, out var f) == true && f is FontFamily ff ? ff : FontFamily.Default;
        var eaten = Text(CalorieGoals.K(EatenKcal), (GoalKcal is null ? 34 : 30) * scale, FontWeight.Bold, Resource("Ink"), mono);
        FormattedText? goal = GoalKcal is { } g ? Text("/ " + CalorieGoals.K(g), 16 * scale, FontWeight.Normal, Resource("Muted"), mono) : null;
        var unit = Text("kcal", 14 * scale, FontWeight.Normal, Resource("Muted"), mono);

        // "2,010 / 1,900" on one line when it fits the ring's hole, else the goal on its own line
        var hole = (MacroRingGeometry.Radius - MacroRingGeometry.Stroke) * 2 * scale;
        var oneLine = goal is not null && eaten.Width + 6 * scale + goal.Width <= hole;
        var total = eaten.Height + (goal is not null && !oneLine ? goal.Height : 0) + unit.Height;
        var y = center.Y - total / 2;

        if (oneLine)
        {
            var w = eaten.Width + 6 * scale + goal!.Width;
            var x = center.X - w / 2;
            context.DrawText(eaten, new Point(x, y));
            context.DrawText(goal, new Point(x + eaten.Width + 6 * scale, y + eaten.Height - goal.Height - 2 * scale));
        }
        else
        {
            context.DrawText(eaten, new Point(center.X - eaten.Width / 2, y));
            if (goal is not null) context.DrawText(goal, new Point(center.X - goal.Width / 2, y + eaten.Height));
        }
        context.DrawText(unit, new Point(center.X - unit.Width / 2, y + total - unit.Height));
    }

    private IBrush ColorOf(Macro macro) => Resource(macro switch { Macro.Protein => "MacroProtein", Macro.Carbs => "MacroCarbs", _ => "MacroFat" });

    private IBrush Resource(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var r) && r is IBrush b ? b : Brushes.Gray;

    private static FormattedText Text(string text, double size, FontWeight weight, IBrush brush, FontFamily font) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(font, FontStyle.Normal, weight), size, brush);

    /// <summary>The arc of the stroke's centre line, <paramref name="startDegrees"/> clockwise from 12 o'clock.</summary>
    private static StreamGeometry Arc(Point center, double radius, double startDegrees, double sweepDegrees)
    {
        static Point On(Point c, double r, double degrees)
        {
            var angle = degrees * Math.PI / 180 - Math.PI / 2;
            return new Point(c.X + r * Math.Cos(angle), c.Y + r * Math.Sin(angle));
        }

        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        g.BeginFigure(On(center, radius, startDegrees), false);
        g.ArcTo(On(center, radius, startDegrees + sweepDegrees), new Size(radius, radius), 0, sweepDegrees > 180, SweepDirection.Clockwise);
        g.EndFigure(false);
        return geometry;
    }
}
