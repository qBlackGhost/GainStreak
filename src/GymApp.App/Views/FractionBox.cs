using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace GymApp.App.Views;

/// <summary>Sizes its child to a share of the room it is given (Phase 27): the Calories chart's bars and target band, which must
/// grow with the card, and the macro bars. Vertical: the child is <see cref="Fraction"/> of the height, standing
/// <see cref="BottomFraction"/> above the bottom. Horizontal: <see cref="Fraction"/> of the width from the left. The child's own
/// MinHeight / MinWidth still applies. It asks for no room of its own, so put it in a stretching cell.</summary>
public class FractionBox : Decorator
{
    public static readonly StyledProperty<double> FractionProperty = AvaloniaProperty.Register<FractionBox, double>(nameof(Fraction));
    public static readonly StyledProperty<double> BottomFractionProperty = AvaloniaProperty.Register<FractionBox, double>(nameof(BottomFraction));
    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<FractionBox, Orientation>(nameof(Orientation), Orientation.Vertical);

    static FractionBox() => AffectsArrange<FractionBox>(FractionProperty, BottomFractionProperty, OrientationProperty);

    public double Fraction { get => GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public double BottomFraction { get => GetValue(BottomFractionProperty); set => SetValue(BottomFractionProperty, value); }
    public Orientation Orientation { get => GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        Child?.Measure(availableSize);
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is not { } child) return finalSize;
        var share = double.IsNaN(Fraction) ? 0 : System.Math.Clamp(Fraction, 0, 1);
        if (Orientation == Orientation.Vertical)
        {
            var h = System.Math.Max(finalSize.Height * share, child.MinHeight);
            var bottom = finalSize.Height * System.Math.Clamp(double.IsNaN(BottomFraction) ? 0 : BottomFraction, 0, 1);
            child.Arrange(new Rect(0, System.Math.Max(0, finalSize.Height - bottom - h), finalSize.Width, h));
        }
        else
        {
            child.Arrange(new Rect(0, 0, System.Math.Max(finalSize.Width * share, child.MinWidth), finalSize.Height));
        }
        return finalSize;
    }
}
