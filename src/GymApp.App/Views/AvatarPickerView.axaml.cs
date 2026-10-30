using Avalonia.Controls;
using Avalonia.Input;
using GymApp.App.ViewModels;

namespace GymApp.App.Views;

public partial class AvatarPickerView : UserControl
{
    public AvatarPickerView()
    {
        InitializeComponent();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Cancel(); e.Handled = true; }
        };
        Scrim.PointerPressed += (_, e) => { Cancel(); e.Handled = true; };
    }

    private void Cancel() => (DataContext as AvatarPickerViewModel)?.CancelCommand.Execute(null);
}
