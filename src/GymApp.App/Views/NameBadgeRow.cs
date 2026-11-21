using System;
using Avalonia;
using Avalonia.Controls;

namespace GymApp.App.Views;

/// <summary>
/// A name followed by small things (a featured badge, a link) on one line: the first child is the name and gets
/// whatever width the others leave, so a long name is cut with its own TextTrimming while the badge stays right after
/// a short one. A horizontal StackPanel never limits the name; a left-aligned Grid trimmed names that fit.
/// </summary>
public class NameBadgeRow : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<NameBadgeRow, double>(nameof(Spacing), 6);

    public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    static NameBadgeRow() => AffectsMeasure<NameBadgeRow>(SpacingProperty);

    protected override Size MeasureOverride(Size available)
    {
        if (Children.Count == 0) return default;
        double rest = 0, height = 0;
        for (var i = 1; i < Children.Count; i++)
        {
            var c = Children[i];
            if (!c.IsVisible) continue;
            c.Measure(new Size(double.PositiveInfinity, available.Height));
            rest += Spacing + c.DesiredSize.Width;
            height = Math.Max(height, c.DesiredSize.Height);
        }
        var name = Children[0];
        name.Measure(new Size(Math.Max(0, available.Width - rest), available.Height));
        return new Size(name.DesiredSize.Width + rest, Math.Max(height, name.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size final)
    {
        if (Children.Count == 0) return final;
        double rest = 0;
        for (var i = 1; i < Children.Count; i++)
            if (Children[i].IsVisible) rest += Spacing + Children[i].DesiredSize.Width;
        var name = Children[0];
        var nameWidth = Math.Min(name.DesiredSize.Width, Math.Max(0, final.Width - rest));
        name.Arrange(new Rect(0, 0, nameWidth, final.Height));
        var x = nameWidth;
        for (var i = 1; i < Children.Count; i++)
        {
            var c = Children[i];
            if (!c.IsVisible) continue;
            x += Spacing;
            c.Arrange(new Rect(x, 0, c.DesiredSize.Width, final.Height));
            x += c.DesiredSize.Width;
        }
        return final;
    }
}
