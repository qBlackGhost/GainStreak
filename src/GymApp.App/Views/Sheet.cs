using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;

namespace GymApp.App.Views;

/// <summary>A popup's card: centred at the size its view gives it (Width, MaxHeight, CornerRadius, Margin...) on
/// desktop and tablets, a full-screen sheet on phones (16d). IronlogStyles sets <see cref="IsFullScreen"/> under
/// MainView's "phone" class; the card's own values come back when the screen gets wide again. A dimmed page around it
/// (the nearest Border above it with the "scrim" class) loses its padding too, so the sheet reaches the edges.</summary>
public class Sheet : Border
{
    public static readonly StyledProperty<bool> IsFullScreenProperty =
        AvaloniaProperty.Register<Sheet, bool>(nameof(IsFullScreen));

    public bool IsFullScreen { get => GetValue(IsFullScreenProperty); set => SetValue(IsFullScreenProperty, value); }

    private (double Width, double MaxWidth, double MaxHeight, HorizontalAlignment H, VerticalAlignment V,
        CornerRadius Corners, Thickness Border, Thickness Margin, BoxShadows Shadow)? _card;
    private Thickness? _scrimPadding;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsFullScreenProperty) Apply();
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        if (IsFullScreen) Apply();
    }

    private void Apply()
    {
        var scrim = this.GetLogicalAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("scrim"));
        if (IsFullScreen && _card is null)
        {
            _card = (Width, MaxWidth, MaxHeight, HorizontalAlignment, VerticalAlignment, CornerRadius, BorderThickness,
                Margin, BoxShadow);
            Width = double.NaN;
            MaxWidth = MaxHeight = double.PositiveInfinity;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;
            CornerRadius = default;
            BorderThickness = default;
            Margin = default;
            BoxShadow = default;
            if (scrim is not null) { _scrimPadding = scrim.Padding; scrim.Padding = default; }
        }
        else if (!IsFullScreen && _card is { } c)
        {
            (Width, MaxWidth, MaxHeight, HorizontalAlignment, VerticalAlignment, CornerRadius, BorderThickness, Margin,
                BoxShadow) = c;
            _card = null;
            if (scrim is not null && _scrimPadding is { } p) scrim.Padding = p;
            _scrimPadding = null;
        }
    }
}
