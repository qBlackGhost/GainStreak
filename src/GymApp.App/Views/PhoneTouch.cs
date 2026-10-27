using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace GymApp.App.Views;

/// <summary>Touch on the phones (16d), for every screen at once:
/// <list type="bullet">
/// <item>Every button (check boxes and toggles too) is at least <see cref="MinTarget"/> × <see cref="MinTarget"/>. Many
/// screens give their buttons a smaller size of their own, which a style can't override, so this sets the minimum at
/// a priority above it. The desktop keeps the sizes it has.</item>
/// <item>A tooltip, which needs a mouse to hover, opens on a long press instead and closes with the next touch.</item>
/// </list></summary>
public static class PhoneTouch
{
    /// <summary>Apple's and Google's smallest comfortable tap target, in points.</summary>
    public const double MinTarget = 44;

    private static Control? _openTip;

    /// <summary>Once, on the phones, before the first view is made.</summary>
    public static void Install()
    {
        Control.LoadedEvent.AddClassHandler<Button>((button, _) => Enlarge(button));
        InputElement.HoldingEvent.AddClassHandler<Control>(OnHolding, handledEventsToo: true);
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>((_, _) => CloseTip(),
            RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private static void Enlarge(Button button)
    {
        if (button.MinHeight < MinTarget) button.SetValue(Layoutable.MinHeightProperty, MinTarget, BindingPriority.Animation);
        if (button.MinWidth < MinTarget) button.SetValue(Layoutable.MinWidthProperty, MinTarget, BindingPriority.Animation);
    }

    // The holding event bubbles from what was pressed; the nearest control with a tooltip shows it
    private static void OnHolding(Control control, HoldingRoutedEventArgs e)
    {
        if (e.HoldingState != HoldingState.Started || e.Handled || !ReferenceEquals(control, e.Source)) return;
        for (Visual? v = control; v is not null; v = v.GetVisualParent())
        {
            if (v is Control c && ToolTip.GetTip(c) is not null)
            {
                CloseTip();
                ToolTip.SetIsOpen(c, true);
                _openTip = c;
                e.Handled = true;
                return;
            }
        }
    }

    private static void CloseTip()
    {
        if (_openTip is null) return;
        ToolTip.SetIsOpen(_openTip, false);
        _openTip = null;
    }
}
