using Avalonia;
using Avalonia.Controls;

namespace GymApp.App.Views;

/// <summary>A Grid laid out differently on phones (16e), since a style can't change a Grid's columns. GainStreakStyles
/// sets <see cref="IsPhone"/> under MainView's "phone" class. Then either <see cref="PhoneColumns"/> (and
/// <see cref="PhoneColumnSpacing"/>, when set) replace the columns, or, with <see cref="PhoneStack"/>, the children go
/// one under another in their markup order (a form's label above its field). The Grid's own layout comes back when the
/// screen widens.</summary>
public class PhoneGrid : Grid
{
    public static readonly StyledProperty<bool> IsPhoneProperty =
        AvaloniaProperty.Register<PhoneGrid, bool>(nameof(IsPhone));

    public static readonly StyledProperty<string?> PhoneColumnsProperty =
        AvaloniaProperty.Register<PhoneGrid, string?>(nameof(PhoneColumns));

    public static readonly StyledProperty<double> PhoneColumnSpacingProperty =
        AvaloniaProperty.Register<PhoneGrid, double>(nameof(PhoneColumnSpacing), double.NaN);

    public static readonly StyledProperty<bool> PhoneStackProperty =
        AvaloniaProperty.Register<PhoneGrid, bool>(nameof(PhoneStack));

    public static readonly StyledProperty<double> PhoneStackSpacingProperty =
        AvaloniaProperty.Register<PhoneGrid, double>(nameof(PhoneStackSpacing), 8);

    static PhoneGrid() => AffectsMeasure<PhoneGrid>(IsPhoneProperty, PhoneStackProperty, PhoneStackSpacingProperty);

    public bool IsPhone { get => GetValue(IsPhoneProperty); set => SetValue(IsPhoneProperty, value); }
    public bool PhoneStack { get => GetValue(PhoneStackProperty); set => SetValue(PhoneStackProperty, value); }
    public double PhoneStackSpacing { get => GetValue(PhoneStackSpacingProperty); set => SetValue(PhoneStackSpacingProperty, value); }

    private bool Stacked => IsPhone && PhoneStack;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (!Stacked) return base.MeasureOverride(availableSize);
        double width = 0, height = 0;
        var first = true;
        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            width = System.Math.Max(width, child.DesiredSize.Width);
            height += child.DesiredSize.Height + (first ? 0 : PhoneStackSpacing);
            first = false;
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (!Stacked) return base.ArrangeOverride(finalSize);
        double y = 0;
        var first = true;
        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            if (!first) y += PhoneStackSpacing;
            first = false;
            child.Arrange(new Rect(0, y, finalSize.Width, child.DesiredSize.Height));
            y += child.DesiredSize.Height;
        }
        return finalSize;
    }
    public string? PhoneColumns { get => GetValue(PhoneColumnsProperty); set => SetValue(PhoneColumnsProperty, value); }
    public double PhoneColumnSpacing { get => GetValue(PhoneColumnSpacingProperty); set => SetValue(PhoneColumnSpacingProperty, value); }

    private (ColumnDefinitions Columns, double Spacing)? _wide;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPhoneProperty) Apply();
    }

    private void Apply()
    {
        if (IsPhone && _wide is null && PhoneColumns is { } columns)
        {
            _wide = (ColumnDefinitions, ColumnSpacing);
            ColumnDefinitions = ColumnDefinitions.Parse(columns);
            if (!double.IsNaN(PhoneColumnSpacing)) ColumnSpacing = PhoneColumnSpacing;
        }
        else if (!IsPhone && _wide is { } wide)
        {
            (ColumnDefinitions, ColumnSpacing) = wide;
            _wide = null;
        }
    }
}
