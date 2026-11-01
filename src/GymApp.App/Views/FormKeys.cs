using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace GymApp.App.Views;

/// <summary>On a phone form, the keyboard's return key on a field whose <c>TextInputOptions.ReturnKeyType</c> is Next or
/// Done does what it says, which the system handles, and no more: this stops it also pressing the form's default button.
/// Any other field, and the desktop's Enter, still submit. A press on Show password never scrolls the page: a finger
/// that drifts a little would move the form under it.</summary>
internal static class FormKeys
{
    public static void Attach(Control form)
    {
        form.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        form.AddHandler(InputElement.PointerPressedEvent, StopGesture, RoutingStrategies.Tunnel);
        form.AddHandler(InputElement.PointerMovedEvent, StopGesture, RoutingStrategies.Tunnel);
        form.AddHandler(InputElement.PointerReleasedEvent, StopGesture, RoutingStrategies.Tunnel);
    }

    private static void StopGesture(object? sender, PointerEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is { } button && button.Classes.Contains("showPassword"))
            e.PreventGestureRecognition();
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not Control form || !MainView.IsPhoneLayout(form)) return;
        if ((e.Source as Visual)?.FindAncestorOfType<TextBox>(includeSelf: true) is { } box
            && TextInputOptions.GetReturnKeyType(box) is TextInputReturnKeyType.Next or TextInputReturnKeyType.Done)
            e.Handled = true;
    }
}
