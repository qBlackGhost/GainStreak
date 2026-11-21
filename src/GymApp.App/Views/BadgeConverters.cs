using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace GymApp.App.Views;

/// <summary>Small converters for the badge screens (Phase 25): which segment of a three-way pill is on.</summary>
public static class BadgeConverters
{
    public static readonly IValueConverter IsZero = new FuncValueConverter<int, bool>(i => i == 0);
    public static readonly IValueConverter IsOne = new FuncValueConverter<int, bool>(i => i == 1);
    public static readonly IValueConverter IsTwo = new FuncValueConverter<int, bool>(i => i == 2);

    /// <summary>A level's bar on the unlock sheet in that level's material (Steel grey, Lime, Fire, Blaze), left to
    /// right as on the badge's rim.</summary>
    public static readonly IValueConverter LookBar = new FuncValueConverter<BadgeLook, IBrush?>(look =>
    {
        if (!BadgeArt.Materials.TryGetValue(look, out var m)) return null;
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        };
        for (var i = 0; i < m.Stops.Length; i++)
            brush.GradientStops.Add(new GradientStop(Color.Parse(m.Stops[i]), m.Stops.Length == 1 ? 0 : (double)i / (m.Stops.Length - 1)));
        return brush;
    });

    /// <summary>A tier not reached yet is drawn faded in the detail's ladder.</summary>
    public static readonly IValueConverter Dim = new FuncValueConverter<bool, double>(reached => reached ? 1 : 0.45);
}
