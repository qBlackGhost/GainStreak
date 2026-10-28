using Avalonia.Controls;
using Avalonia.Interactivity;

namespace GymApp.App.Views;

public partial class RegisterView : UserControl
{
    public RegisterView()
    {
        InitializeComponent();
    }

    private void OnReveal(object? sender, RoutedEventArgs e) => PasswordReveal.OnClick(sender, e);
}
