using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using GymApp.Core;

namespace GymApp.App.Views;

/// <summary>
/// A user's avatar: a circle in their chosen colour with an icon or their initials, or the plain initials on
/// <see cref="Fallback"/> with no choice (or one a newer build made). With a <see cref="Level"/> it has a ring that
/// shows how far they've come; 0 leaves it out, and the circle fills the control.
/// </summary>
public class AvatarView : Control
{
    private const double Ring = 2, Gap = 2;

    private static readonly Dictionary<string, Color> Colors = new()
    {
        ["black"] = Color.Parse("#232323"),
        ["red"] = Color.Parse("#D8483C"),
        ["orange"] = Color.Parse("#D97F22"),
        ["green"] = Color.Parse("#2F9E6E"),
        ["teal"] = Color.Parse("#1F93A3"),
        ["blue"] = Color.Parse("#2F7FD6"),
        ["violet"] = Color.Parse("#6D4AD6"),
        ["pink"] = Color.Parse("#C9478F"),
        ["slate"] = Color.Parse("#5B6472"),
    };

    public static readonly StyledProperty<string?> ChoiceProperty = AvaloniaProperty.Register<AvatarView, string?>(nameof(Choice));
    public static readonly StyledProperty<string?> InitialsProperty = AvaloniaProperty.Register<AvatarView, string?>(nameof(Initials));
    public static readonly StyledProperty<int> LevelProperty = AvaloniaProperty.Register<AvatarView, int>(nameof(Level));
    public static readonly StyledProperty<IBrush?> FallbackProperty = AvaloniaProperty.Register<AvatarView, IBrush?>(nameof(Fallback));
    public static readonly StyledProperty<IBrush?> FallbackInkProperty = AvaloniaProperty.Register<AvatarView, IBrush?>(nameof(FallbackInk));

    static AvatarView() =>
        AffectsRender<AvatarView>(ChoiceProperty, InitialsProperty, LevelProperty, FallbackProperty, FallbackInkProperty);

    public string? Choice { get => GetValue(ChoiceProperty); set => SetValue(ChoiceProperty, value); }
    public string? Initials { get => GetValue(InitialsProperty); set => SetValue(InitialsProperty, value); }
    public int Level { get => GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    /// <summary>The circle and the initials with no choice; the app's neutral fill and ink by default.</summary>
    public IBrush? Fallback { get => GetValue(FallbackProperty); set => SetValue(FallbackProperty, value); }
    public IBrush? FallbackInk { get => GetValue(FallbackInkProperty); set => SetValue(FallbackInkProperty, value); }

    public AvatarView() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();   // the brushes change with the theme

    private IBrush Brush(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var r) && r is IBrush b ? b : fallback;

    private IBrush RingBrush(int level) => AvatarRings.For(level) switch
    {
        AvatarRing.Gold => new SolidColorBrush(Color.Parse("#F0C24B")),
        AvatarRing.Lime => Brush("AccentInk", Brushes.YellowGreen),
        AvatarRing.Silver => new SolidColorBrush(Color.Parse("#A8A8A2")),
        _ => Brush("Control", Brushes.DimGray),
    };

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = size / 2;

        if (Level > 0)
        {
            context.DrawEllipse(null, new Pen(RingBrush(Level), Ring), center, radius - Ring / 2, radius - Ring / 2);
            radius -= Ring + Gap;
        }

        var choice = AvatarChoice.Parse(Choice);
        var fill = choice is null ? Fallback ?? Brush("Fill3", Brushes.DimGray) : new SolidColorBrush(Colors[choice.Color]);
        var ink = choice is null ? FallbackInk ?? Brush("Ink", Brushes.White) : Brushes.White;
        context.DrawEllipse(fill, null, center, radius, radius);

        if (choice?.Icon is { } icon && this.TryFindResource(IconKey(icon), ActualThemeVariant, out var r) && r is Geometry geometry)
        {
            var scale = radius * 1.15 / 24;   // the icons are drawn on a 24 px square
            var at = Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(center.X - 12 * scale, center.Y - 12 * scale);
            using (context.PushTransform(at))
                context.DrawGeometry(null, new Pen(ink, 2.2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
            return;
        }

        if (string.IsNullOrEmpty(Initials)) return;
        var text = new FormattedText(Initials, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(TextElement.GetFontFamily(this), FontStyle.Normal, FontWeight.Bold), radius * 0.75, ink);
        context.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }

    // "flame" → IcFlame, in IronlogResources
    private static string IconKey(string icon) => "Ic" + char.ToUpperInvariant(icon[0]) + icon[1..];
}
