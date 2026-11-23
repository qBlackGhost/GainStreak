using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using GymApp.Core;

namespace GymApp.App.Views;

/// <summary>
/// The phones' weight chart (Phase 27), after Apple Health's: a header with the average and the dates, a line through the
/// weigh-ins of one week, month or six months, and day labels. Touch and hold, then slide: the day under the thumb is
/// highlighted (its whole column, not just the dot) and the header shows its weight. A quick swipe right shows the window before,
/// left the one after. A tap shows a day for a moment. Window maths: <see cref="WeightChartModel"/>.
/// </summary>
public class WeightTrendChart : Control
{
    private const double HeaderHeight = 64, AxisHeight = 22, ScaleWidth = 30, HoldMs = 180, MoveSlop = 10, SwipeDistance = 40;

    public static readonly StyledProperty<IReadOnlyList<(DateOnly Date, double Value)>?> WeighInsProperty =
        AvaloniaProperty.Register<WeightTrendChart, IReadOnlyList<(DateOnly Date, double Value)>?>(nameof(WeighIns));
    public static readonly StyledProperty<WeightRange> RangeProperty =
        AvaloniaProperty.Register<WeightTrendChart, WeightRange>(nameof(Range));
    public static readonly StyledProperty<string> UnitProperty =
        AvaloniaProperty.Register<WeightTrendChart, string>(nameof(Unit), "kg");

    static WeightTrendChart() => AffectsRender<WeightTrendChart>(WeighInsProperty, RangeProperty, UnitProperty);

    /// <summary>Every weigh-in, in the shown unit.</summary>
    public IReadOnlyList<(DateOnly Date, double Value)>? WeighIns { get => GetValue(WeighInsProperty); set => SetValue(WeighInsProperty, value); }
    public WeightRange Range { get => GetValue(RangeProperty); set => SetValue(RangeProperty, value); }
    public string Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    private int _page;
    private int? _held;
    private Point _down;
    private bool _pressed;
    private enum Mode { None, Scrub, Swipe }
    private Mode _mode;
    private readonly DispatcherTimer _hold = new() { Interval = TimeSpan.FromMilliseconds(HoldMs) };
    private readonly DispatcherTimer _peek = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private IPointer? _pointer;

    public WeightTrendChart()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        _hold.Tick += (_, _) =>
        {
            _hold.Stop();
            if (!_pressed || _mode != Mode.None) return;
            _mode = Mode.Scrub;
            _pointer?.Capture(this);
            Hold(_down.X);
        };
        _peek.Tick += (_, _) => { _peek.Stop(); _held = null; InvalidateVisual(); };
        AutomationProperties.SetName(this, "Weight chart");
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RangeProperty || change.Property == WeighInsProperty)
        {
            if (change.Property == RangeProperty) _page = 0;
            _held = null;
            UpdateName();
        }
    }

    private WeightWindow Current() =>
        WeightChartModel.Window(WeighIns ?? [], Range, AppClock.Today, _page);

    private Rect Plot => new(0, HeaderHeight, Math.Max(0, Bounds.Width - ScaleWidth), Math.Max(0, Bounds.Height - HeaderHeight - AxisHeight));

    // ----- Gestures -----

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _down = e.GetPosition(this);
        _pressed = true;
        _mode = Mode.None;
        _pointer = e.Pointer;
        _peek.Stop();
        _hold.Start();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_pressed) return;
        var p = e.GetPosition(this);
        if (_mode == Mode.Scrub) { Hold(p.X); e.Handled = true; return; }
        if (_mode != Mode.None) return;
        var dx = Math.Abs(p.X - _down.X);
        var dy = Math.Abs(p.Y - _down.Y);
        if (dx > MoveSlop && dx > dy)
        {
            _hold.Stop();
            _mode = Mode.Swipe;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
        else if (dy > MoveSlop)
        {
            _hold.Stop();      // a vertical pan: the page scrolls, the chart lets go
            _pressed = false;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _hold.Stop();
        if (!_pressed) return;
        _pressed = false;
        var p = e.GetPosition(this);
        switch (_mode)
        {
            case Mode.Swipe:
                var w = Current();
                if (p.X - _down.X > SwipeDistance && w.HasOlder) _page++;
                else if (_down.X - p.X > SwipeDistance && w.HasNewer) _page--;
                _held = null;
                UpdateName();
                break;
            case Mode.Scrub:
                _held = null;
                break;
            default:   // a tap: show that day for a moment
                Hold(p.X);
                _peek.Start();
                break;
        }
        _mode = Mode.None;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _hold.Stop();
        if (_mode == Mode.Scrub) _held = null;
        _pressed = false;
        _mode = Mode.None;
        InvalidateVisual();
    }

    private void Hold(double x)
    {
        var plot = Plot;
        if (plot.Width <= 0) return;
        var slot = WeightChartModel.SlotAt(Range, (x - plot.X) / plot.Width);
        if (slot == _held) return;
        _held = slot;
        InvalidateVisual();
    }

    private void UpdateName()
    {
        var (label, value, date) = WeightChartModel.Header(Current());
        AutomationProperties.SetName(this, value is null ? $"Weight chart, {date}, no weigh-ins" : $"Weight chart, {date}, average {value} {Unit}");
    }

    // ----- Drawing -----

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= HeaderHeight + AxisHeight) return;
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));   // the whole chart takes touches, not just its drawn parts
        var w = Current();
        var plot = Plot;
        var mono = Application.Current?.TryFindResource("Mono", ActualThemeVariant, out var f) == true && f is FontFamily ff ? ff : FontFamily.Default;
        var ink = Res("Ink");
        var muted = Res("Muted");
        var faint = Res("Faint");
        var accent = Res("Accent");

        // Header: AVERAGE (or the held day), the value, the dates
        var (label, value, date) = _held is { } h ? WeightChartModel.Header(w, h) : WeightChartModel.Header(w);
        context.DrawText(Text(label, 11, FontWeight.SemiBold, muted, FontFamily.Default), new Point(0, 0));
        var number = Text(value ?? "—", 28, FontWeight.Bold, ink, mono);
        context.DrawText(number, new Point(0, 15));
        if (value is not null) context.DrawText(Text(" " + Unit, 14, FontWeight.Normal, muted, FontFamily.Default), new Point(number.Width + 2, 28));
        context.DrawText(Text(date, 12, FontWeight.Normal, muted, FontFamily.Default), new Point(0, 47));

        // Scale: three lines with their values on the right
        double Y(double v) => plot.Y + 8 + (w.High - v) / (w.High - w.Low) * (plot.Height - 16);
        var grid = new Pen(Res("Line"), 1);
        foreach (var g in new[] { w.Low, (w.Low + w.High) / 2, w.High })
        {
            var y = Math.Round(Y(g)) + 0.5;
            context.DrawLine(grid, new Point(0, y), new Point(plot.Right, y));
            var t = Text(g.ToString("0.#", CultureInfo.InvariantCulture), 10, FontWeight.Normal, faint, mono);
            context.DrawText(t, new Point(Bounds.Width - t.Width, y - t.Height / 2));
        }

        var n = w.Slots.Count;
        var slotWidth = plot.Width / n;
        double X(int i) => plot.X + slotWidth * (i + 0.5);

        // The held column, and a line down it
        if (_held is { } held)
        {
            context.DrawRectangle(Res("Fill"), null, new RoundedRect(new Rect(plot.X + slotWidth * held + 1, plot.Y, slotWidth - 2, plot.Height), 6));
            context.DrawLine(new Pen(muted, 1, new DashStyle([3, 3], 0)), new Point(X(held), plot.Y), new Point(X(held), plot.Bottom));
        }

        // The line through the weigh-ins (across days without one), and a dot on each
        var points = w.Slots.Where(s => s.Value is not null).Select(s => (s.Index, P: new Point(X(s.Index), Y(s.Value!.Value)))).ToList();
        if (points.Count > 1)
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(points[0].P, false);
                foreach (var p in points.Skip(1)) g.LineTo(p.P);
                g.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(accent, 2.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geo);
        }
        var dot = Range == WeightRange.Month ? 2.5 : 4;
        foreach (var (index, p) in points)
            context.DrawEllipse(accent, null, p, index == _held ? dot + 3 : dot, index == _held ? dot + 3 : dot);

        // Day labels; today's is brighter
        var labels = WeightChartModel.AxisLabels(w);
        for (var i = 0; i < n; i++)
        {
            if (labels[i] == "") continue;
            var isToday = w.Slots[i].Start <= AppClock.Today && AppClock.Today <= w.Slots[i].End && Range == WeightRange.Week;
            var t = Text(labels[i], 11, isToday ? FontWeight.Bold : FontWeight.Normal, isToday ? ink : muted, FontFamily.Default);
            var x = Range == WeightRange.Week ? X(i) - t.Width / 2 : Math.Clamp(X(i) - t.Width / 2, 0, plot.Right - t.Width);
            context.DrawText(t, new Point(x, plot.Bottom + 5));
        }
    }

    private IBrush Res(string key) => this.TryFindResource(key, ActualThemeVariant, out var r) && r is IBrush b ? b : Brushes.Gray;

    private static FormattedText Text(string text, double size, FontWeight weight, IBrush brush, FontFamily font) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(font, FontStyle.Normal, weight), size, brush);
}
