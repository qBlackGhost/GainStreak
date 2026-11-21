using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using GymApp.Core;

namespace GymApp.App.Views;

/// <summary>What a badge is drawn in (Phase 25): a tier's material, a fixed one, or locked / hidden.</summary>
/// <summary>How a badge is drawn. <see cref="Secret"/>: a friend's featured hidden badge you haven't earned, the
/// "?" in a lighter grey than <see cref="Hidden"/> so it still reads at 20 px on a dark row.</summary>
public enum BadgeLook { Steel, Lime, Fire, Blaze, Ice, Limited, Locked, Hidden, Secret }

public static class BadgeLooks
{
    /// <summary>The look of <paramref name="badge"/> at <paramref name="tier"/>. <paramref name="masked"/>: a hidden
    /// badge the viewer hasn't earned, drawn as the dashed "?".</summary>
    public static BadgeLook For(BadgeDefinition badge, bool earned, int tier, bool masked = false) =>
        masked ? BadgeLook.Hidden
        : !earned ? BadgeLook.Locked
        : badge.Material == BadgeMaterial.Ice ? BadgeLook.Ice
        : badge.Material == BadgeMaterial.Limited ? BadgeLook.Limited
        : badge.IsTiered ? (BadgeLook)Math.Clamp(tier - 1, 0, 3)
        : BadgeLook.Lime;
}

/// <summary>
/// One badge, drawn on a 120 × 120 grid scaled to the control (docs/PHASE_25_BADGES.md, "Appendix: drawing a badge"):
/// a hexagon (round for the limited Monthly Challenge) in its material's gradient, a dark core, and a 24-grid glyph
/// or a short text. Never Skip Leg Day adds muscle lines and twinkling shine lines from 40 px up.
/// </summary>
public class BadgeView : Control
{
    public static readonly StyledProperty<string?> GlyphProperty = AvaloniaProperty.Register<BadgeView, string?>(nameof(Glyph));
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<BadgeView, string?>(nameof(Text));
    public static readonly StyledProperty<BadgeLook> LookProperty = AvaloniaProperty.Register<BadgeView, BadgeLook>(nameof(Look));

    static BadgeView() => AffectsRender<BadgeView>(GlyphProperty, TextProperty, LookProperty);

    public string? Glyph { get => GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public BadgeLook Look { get => GetValue(LookProperty); set => SetValue(LookProperty, value); }

    private static readonly Geometry Hex = StreamGeometry.Parse("M60 6L106.8 33V87L60 114L13.2 87V33Z");
    private static readonly Geometry HexInner = StreamGeometry.Parse("M60 18L96.4 39V81L60 102L23.6 81V39Z");
    private static readonly Geometry Round = new EllipseGeometry(new Rect(6, 6, 108, 108));
    private static readonly Geometry RoundInner = new EllipseGeometry(new Rect(18, 18, 84, 84));

    private static readonly Dictionary<string, string> DetailPaths = new()
    {
        ["leg"] = "M9 5.6C11.5 6.5 13.6 8 14.8 10M14.8 10.2C15.4 11 15.4 12 14.8 12.8M12.6 13.4C13.4 14.8 13.4 16.6 12.8 18.2M17 21.4v-.9M18.2 21.4v-.7",
    };

    private static readonly Dictionary<string, string> ShinePaths = new()
    {
        ["leg"] = "M10 1.2l.4 1.2M13.6 1.9l-.3 1.2M17.4 4.4l-1 .8M19.8 9.4l-1.2.3M19.9 12.6l-1.2-.4M8.4 15l1 .3M8.3 12.4l1.1.5",
    };

    private static readonly Dictionary<string, Geometry> Parsed = [];

    private static Geometry? Path(Dictionary<string, string> paths, string? key)
    {
        if (key is null || !paths.TryGetValue(key, out var d)) return null;
        var cacheKey = $"{paths.GetHashCode()}:{key}";
        if (!Parsed.TryGetValue(cacheKey, out var g)) Parsed[cacheKey] = g = StreamGeometry.Parse(d);
        return g;
    }

    /// <summary>Whether this glyph has a shine (Never Skip Leg Day).</summary>
    public static bool HasShine(string? glyph) => glyph is not null && ShinePaths.ContainsKey(glyph);

    private const double MinShineSize = 40;
    private static readonly TimeSpan ShineCycle = TimeSpan.FromSeconds(2.4);
    private DispatcherTimer? _shineTimer;
    private DateTime _shineStart;
    private bool _attached;

    private bool Shines => HasShine(Glyph) && string.IsNullOrEmpty(Text) && Look is not (BadgeLook.Locked or BadgeLook.Hidden or BadgeLook.Secret)
                           && Math.Min(Bounds.Width, Bounds.Height) >= MinShineSize;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        UpdateShineTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        _shineTimer?.Stop();
        _shineTimer = null;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GlyphProperty || change.Property == LookProperty || change.Property == TextProperty
            || change.Property == BoundsProperty)
            UpdateShineTimer();
    }

    /// <summary>The twinkle: a light timer, only while a shining badge is on screen.</summary>
    private void UpdateShineTimer()
    {
        var wanted = Shines && _attached;
        if (wanted && _shineTimer is null)
        {
            _shineStart = DateTime.UtcNow;
            _shineTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(80), DispatcherPriority.Background, (_, _) => InvalidateVisual());
            _shineTimer.Start();
        }
        else if (!wanted && _shineTimer is not null)
        {
            _shineTimer.Stop();
            _shineTimer = null;
        }
    }

    private double ShineOpacity =>
        _shineTimer is null ? 1
        : 0.625 + 0.375 * Math.Cos(2 * Math.PI * ((DateTime.UtcNow - _shineStart).TotalSeconds % ShineCycle.TotalSeconds) / ShineCycle.TotalSeconds);

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;
        var m = BadgeArt.Materials[Look];
        var scale = size / 120;
        var offset = Matrix.CreateTranslation((Bounds.Width - size) / 2 / scale, (Bounds.Height - size) / 2 / scale);

        using (context.PushTransform(offset * Matrix.CreateScale(scale, scale)))
        {
            if (m.Glow is { } glow)
            {
                var halo = new RadialGradientBrush
                {
                    GradientStops = { new GradientStop(Color.Parse(glow), 0.55), new GradientStop(Colors.Transparent, 1) },
                };
                context.DrawEllipse(halo, null, new Point(60, 60), 66, 66);
            }

            var ring = Gradient(m.Stops);
            var round = Look == BadgeLook.Limited;
            context.DrawGeometry(ring, new Pen(ring, 8, lineJoin: PenLineJoin.Round), round ? Round : Hex);
            var linePen = new Pen(Brush(m.Line), 1.5, lineJoin: PenLineJoin.Round,
                dashStyle: Look is BadgeLook.Hidden or BadgeLook.Secret ? new DashStyle([4 / 1.5, 4 / 1.5], 0) : null);
            context.DrawGeometry(Brush(m.Core), linePen, round ? RoundInner : HexInner);

            IBrush ink = m.GlyphColor is { } flat ? Brush(flat) : Gradient(m.Stops);
            var text = Look is BadgeLook.Hidden or BadgeLook.Secret ? "?" : Text;
            if (!string.IsNullOrEmpty(text))
            {
                var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Cascadia Mono, Menlo, Consolas, monospace"), FontStyle.Normal, FontWeight.Bold),
                    text.Length >= 4 ? 21 : 25, ink);
                context.DrawText(ft, new Point(60 - ft.Width / 2, 60 - ft.Height / 2));
                return;
            }

            using (context.PushTransform(Matrix.CreateScale(1.6, 1.6) * Matrix.CreateTranslation(40.8, 40.8)))
            {
                if (Path(BadgeArt.GlyphPaths, Glyph) is { } glyph)
                    context.DrawGeometry(null, new Pen(ink, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), glyph);
                if (Path(DetailPaths, Glyph) is { } detail)
                    context.DrawGeometry(null, new Pen(ink, 1.1, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), detail);
                if (Shines && Path(ShinePaths, Glyph) is { } shine)
                    using (context.PushOpacity(ShineOpacity))
                        context.DrawGeometry(null, new Pen(Brushes.White, 1.2, lineCap: PenLineCap.Round), shine);
            }
        }
    }

    private static SolidColorBrush Brush(string color) => new(Color.Parse(color));

    private static LinearGradientBrush Gradient(string[] stops) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse(stops[0]), 0),
            new GradientStop(Color.Parse(stops[1]), 0.5),
            new GradientStop(Color.Parse(stops[2]), 1),
        },
    };
}
