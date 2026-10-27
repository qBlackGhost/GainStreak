using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace GymApp.App.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
    }

    // The phones' eye button: show the password as typed, and back
    private void OnReveal(object? sender, RoutedEventArgs e)
    {
        PasswordBox.RevealPassword = !PasswordBox.RevealPassword;
        var shown = PasswordBox.RevealPassword;
        RevealIcon.Data = this.FindResource(shown ? "IcEyeOff" : "IcEye") as Geometry;
        AutomationProperties.SetName(RevealButton, shown ? "Hide password" : "Show password");
    }
}
