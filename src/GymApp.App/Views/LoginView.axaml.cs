using Avalonia.Controls;
using Avalonia.Interactivity;

namespace GymApp.App.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
    }

    private void OnReveal(object? sender, RoutedEventArgs e) => PasswordReveal.OnClick(sender, e);
}
