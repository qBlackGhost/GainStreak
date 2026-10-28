using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace GymApp.App.Views;

/// <summary>The phones' eye button inside a password box (Log in, Create account): it sits in the same Panel as the
/// box ("reveal" class) and shows the password as typed, and back.</summary>
public static class PasswordReveal
{
    public static void OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Parent: Panel panel } button || panel.Children.OfType<TextBox>().FirstOrDefault() is not { } box)
            return;
        box.RevealPassword = !box.RevealPassword;
        var shown = box.RevealPassword;
        if (button.Content is Icon icon) icon.Data = button.FindResource(shown ? "IcEyeOff" : "IcEye") as Geometry;
        AutomationProperties.SetName(button, shown ? "Hide password" : "Show password");
    }
}
