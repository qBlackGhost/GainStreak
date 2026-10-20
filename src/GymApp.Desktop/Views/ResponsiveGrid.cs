using System;
using Avalonia;
using Avalonia.Controls;

namespace GymApp.Desktop.Views;

/// <summary>Equal-width columns, as many as fit at <see cref="MinItemWidth"/> (CSS "repeat(auto-fill, minmax(w, 1fr))").
/// Items in a row share the row's height.</summary>
public class ResponsiveGrid : Panel
{
    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<ResponsiveGrid, double>(nameof(MinItemWidth), 320);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<ResponsiveGrid, double>(nameof(Spacing), 16);

    /// <summary>Only full rows: drops to fewer columns rather than leave a short last row (4 items: 4, 2 × 2 or 1, never 3 + 1).</summary>
    public static readonly StyledProperty<bool> BalancedProperty =
        AvaloniaProperty.Register<ResponsiveGrid, bool>(nameof(Balanced));

    static ResponsiveGrid() => AffectsMeasure<ResponsiveGrid>(MinItemWidthProperty, SpacingProperty, BalancedProperty);

    public double MinItemWidth { get => GetValue(MinItemWidthProperty); set => SetValue(MinItemWidthProperty, value); }
    public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }
    public bool Balanced { get => GetValue(BalancedProperty); set => SetValue(BalancedProperty, value); }

    private (int Columns, double ItemWidth) Columns(double width)
    {
        if (double.IsInfinity(width)) return (1, MinItemWidth);
        var columns = Math.Max(1, (int)Math.Floor((width + Spacing) / (MinItemWidth + Spacing)));
        if (Balanced && Children.Count > 0)
        {
            columns = Math.Min(columns, Children.Count);
            while (columns > 1 && Children.Count % columns != 0) columns--;
        }
        return (columns, Math.Max(0, (width - Spacing * (columns - 1)) / columns));
    }

    protected override Size MeasureOverride(Size available)
    {
        var (columns, itemWidth) = Columns(available.Width);
        double height = 0, rowHeight = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            child.Measure(new Size(itemWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            if (i % columns == columns - 1 || i == Children.Count - 1)
            {
                height += rowHeight + (height > 0 ? Spacing : 0);
                rowHeight = 0;
            }
        }
        var width = double.IsInfinity(available.Width) ? itemWidth : available.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var (columns, itemWidth) = Columns(final.Width);
        double y = 0;
        for (var row = 0; row * columns < Children.Count; row++)
        {
            double rowHeight = 0;
            for (var c = 0; c < columns && row * columns + c < Children.Count; c++)
                rowHeight = Math.Max(rowHeight, Children[row * columns + c].DesiredSize.Height);
            for (var c = 0; c < columns && row * columns + c < Children.Count; c++)
                Children[row * columns + c].Arrange(new Rect(c * (itemWidth + Spacing), y, itemWidth, rowHeight));
            y += rowHeight + Spacing;
        }
        return final;
    }
}
