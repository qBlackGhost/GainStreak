using Avalonia.Controls;
using Avalonia.Input;
using GymApp.App.ViewModels;

namespace GymApp.App.Views;

public partial class BadgeUnlockView : UserControl
{
    public BadgeUnlockView()
    {
        InitializeComponent();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
        Scrim.PointerPressed += (_, e) => { Close(); e.Handled = true; };
    }

    private void Close() => (DataContext as BadgeUnlockViewModel)?.CloseCommand.Execute(null);
}
