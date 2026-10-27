using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;

namespace GymApp.App.Views;

/// <summary>A popup's card: centred at the size its view gives it (Width, MaxHeight, CornerRadius, Margin...) on
/// desktop and tablets, a sheet from the bottom on phones (the GainStreak Mobile canvas): full width, rounded on top,
/// as tall as its content up to <see cref="MaxShare"/> of the screen. IronlogStyles sets <see cref="IsBottomSheet"/>
/// under MainView's "phone" class; the card's own values come back when the screen gets wide again. A dimmed page
/// around it (the nearest Border above it with the "scrim" class) loses its padding too, so the sheet reaches the
/// edges.</summary>
public class Sheet : Border
{
    public static readonly StyledProperty<bool> IsBottomSheetProperty =
        AvaloniaProperty.Register<Sheet, bool>(nameof(IsBottomSheet));

    public bool IsBottomSheet { get => GetValue(IsBottomSheetProperty); set => SetValue(IsBottomSheetProperty, value); }

    /// <summary>The most of the screen a bottom sheet takes; the dimmed page shows above it.</summary>
    public const double MaxShare = 0.88;

    private (double Width, double MaxWidth, double MaxHeight, HorizontalAlignment H, VerticalAlignment V,
        CornerRadius Corners, Thickness Border, Thickness Margin, BoxShadows Shadow)? _card;
    private Thickness? _scrimPadding;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsBottomSheetProperty) Apply();
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        if (IsBottomSheet) Apply();
    }

    // A bottom sheet stops short of the top of the screen
    protected override Size MeasureOverride(Size availableSize) =>
        base.MeasureOverride(IsBottomSheet && !double.IsInfinity(availableSize.Height)
            ? availableSize.WithHeight(availableSize.Height * MaxShare) : availableSize);

    private void Apply()
    {
        var scrim = this.GetLogicalAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("scrim"));
        if (IsBottomSheet && _card is null)
        {
            _card = (Width, MaxWidth, MaxHeight, HorizontalAlignment, VerticalAlignment, CornerRadius, BorderThickness,
                Margin, BoxShadow);
            Width = double.NaN;
            MaxWidth = MaxHeight = double.PositiveInfinity;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Bottom;
            CornerRadius = new CornerRadius(22, 22, 0, 0);
            BorderThickness = new Thickness(0, 1, 0, 0);
            Margin = default;
            BoxShadow = default;
            if (scrim is not null) { _scrimPadding = scrim.Padding; scrim.Padding = default; }
        }
        else if (!IsBottomSheet && _card is { } c)
        {
            (Width, MaxWidth, MaxHeight, HorizontalAlignment, VerticalAlignment, CornerRadius, BorderThickness, Margin,
                BoxShadow) = c;
            _card = null;
            if (scrim is not null && _scrimPadding is { } p) scrim.Padding = p;
            _scrimPadding = null;
        }
        InvalidateMeasure();
    }
}
