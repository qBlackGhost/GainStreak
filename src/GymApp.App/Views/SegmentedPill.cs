using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace GymApp.App.Views;

/// <summary>
/// A segmented toggle (the design canvas's "sliding pill"): its children are the options, laid out in one row of equal
/// cells, with transparent backgrounds. The panel draws a rounded pill under the <see cref="SelectedIndex"/> cell and
/// slides it there in 0.32 s when the selection changes; the options fade their own text (see the "seg" styles).
/// A negative <see cref="SelectedIndex"/> shows no pill (nothing chosen yet).
/// It paints the pill as its own Background (a drawing rebuilt as the pill moves) rather than holding it as a child,
/// so it also works as an ItemsControl's items panel; put any track color on a Border around it.
/// </summary>
public class SegmentedPill : Panel
{
    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<SegmentedPill, int>(nameof(SelectedIndex));

    public static readonly StyledProperty<IBrush?> PillBrushProperty =
        AvaloniaProperty.Register<SegmentedPill, IBrush?>(nameof(PillBrush));

    public static readonly StyledProperty<double> PillCornerRadiusProperty =
        AvaloniaProperty.Register<SegmentedPill, double>(nameof(PillCornerRadius), 9);

    /// <summary>The gap between a cell's edge and the pill; match the options' Margin.</summary>
    public static readonly StyledProperty<double> PillInsetProperty =
        AvaloniaProperty.Register<SegmentedPill, double>(nameof(PillInset), 2);

    /// <summary>The pill's left edge in pixels; animated, so not meant to be set from outside.</summary>
    public static readonly StyledProperty<double> PillOffsetProperty =
        AvaloniaProperty.Register<SegmentedPill, double>(nameof(PillOffset));

    /// <summary>Pills that stand for the same switch on two screens (the phones' Log in / Create account, Leaderboard /
    /// Map). When one appears right after another of its group went, its pill slides over from where that one was
    /// instead of starting in place, so the switch looks like one control across the screen change.</summary>
    public static readonly StyledProperty<string?> SlideGroupProperty =
        AvaloniaProperty.Register<SegmentedPill, string?>(nameof(SlideGroup));

    /// <summary>For two-option toggles bound to a bool: false is the first option, true the second.</summary>
    public static readonly IValueConverter BoolToIndex = new FuncValueConverter<bool, int>(b => b ? 1 : 0);

    private static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(320);

    private readonly Transitions _slide =
    [
        new DoubleTransition { Property = PillOffsetProperty, Duration = SlideDuration, Easing = new CubicEaseInOut() }
    ];

    // The last index each group showed, and when (Stopwatch ticks)
    private static readonly System.Collections.Generic.Dictionary<string, (int Index, long At)> Groups = [];
    private static readonly TimeSpan GroupMemory = TimeSpan.FromSeconds(1.5);

    private double _cell;
    private Size _size;   // the arranged size; Bounds isn't updated yet while arranging

    static SegmentedPill()
    {
        AffectsArrange<SegmentedPill>(SelectedIndexProperty);
        SelectedIndexProperty.Changed.AddClassHandler<SegmentedPill>((p, _) => p.Paint());
        PillOffsetProperty.Changed.AddClassHandler<SegmentedPill>((p, _) => p.Paint());
        PillBrushProperty.Changed.AddClassHandler<SegmentedPill>((p, _) => p.Paint());
        PillCornerRadiusProperty.Changed.AddClassHandler<SegmentedPill>((p, _) => p.Paint());
        PillInsetProperty.Changed.AddClassHandler<SegmentedPill>((p, _) => p.Paint());
    }

    public int SelectedIndex { get => GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
    public IBrush? PillBrush { get => GetValue(PillBrushProperty); set => SetValue(PillBrushProperty, value); }
    public double PillCornerRadius { get => GetValue(PillCornerRadiusProperty); set => SetValue(PillCornerRadiusProperty, value); }
    public double PillInset { get => GetValue(PillInsetProperty); set => SetValue(PillInsetProperty, value); }
    public double PillOffset { get => GetValue(PillOffsetProperty); set => SetValue(PillOffsetProperty, value); }
    public string? SlideGroup { get => GetValue(SlideGroupProperty); set => SetValue(SlideGroupProperty, value); }

    protected override Size MeasureOverride(Size available)
    {
        var n = Math.Max(1, Children.Count);
        var cell = double.IsInfinity(available.Width) ? double.PositiveInfinity : available.Width / n;
        double width = 0, height = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(cell, available.Height));
            width = Math.Max(width, child.DesiredSize.Width);
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(width * n, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var n = Math.Max(1, Children.Count);
        var cell = final.Width / n;
        _size = final;
        for (var i = 0; i < Children.Count; i++)
            Children[i].Arrange(new Rect(i * cell, 0, cell, final.Height));

        // A new selection slides; a resize (or the first layout) just moves the pill
        var target = Math.Clamp(SelectedIndex, 0, n - 1) * cell;
        if (Math.Abs(cell - _cell) > 0.5)
        {
            // The first layout: in place, or sliding over from the group's last pill
            var first = _cell <= 0;
            Transitions = null;
            PillOffset = first && FromGroup() is { } from && from != SelectedIndex
                ? Math.Clamp(from, 0, n - 1) * cell
                : target;
            Transitions = _slide;
            PillOffset = target;
            _cell = cell;
        }
        else
        {
            Transitions ??= _slide;
            PillOffset = target;
        }
        Paint();
        if (SlideGroup is { } group) Groups[group] = (SelectedIndex, System.Diagnostics.Stopwatch.GetTimestamp());
        return final;
    }

    private int? FromGroup() =>
        SlideGroup is { } group && Groups.TryGetValue(group, out var last)
        && System.Diagnostics.Stopwatch.GetElapsedTime(last.At) < GroupMemory
            ? last.Index : null;

    /// <summary>The background: a transparent box the panel's size (so nothing stretches) with the pill on it.</summary>
    private void Paint()
    {
        var size = _size;
        if (Children.Count == 0 || SelectedIndex < 0 || PillBrush is null || _cell <= 0 || size.Width <= 0 || size.Height <= 0)
        {
            Background = null;
            return;
        }
        var inset = PillInset;
        var pill = new Rect(PillOffset + inset, inset, Math.Max(0, _cell - 2 * inset), Math.Max(0, size.Height - 2 * inset));
        var drawing = new DrawingGroup
        {
            Children =
            {
                new GeometryDrawing { Brush = Brushes.Transparent, Geometry = new RectangleGeometry(new Rect(size)) },
                new GeometryDrawing
                {
                    Brush = PillBrush,
                    Geometry = new RectangleGeometry(pill, PillCornerRadius, PillCornerRadius)
                }
            }
        };
        Background = new DrawingBrush(drawing) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
    }
}
