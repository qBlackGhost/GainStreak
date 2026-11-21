using Avalonia.Controls;
using Avalonia.Input;
using GymApp.App.ViewModels;

namespace GymApp.App.Views;

public partial class FeaturedBadgePickerView : UserControl
{
    public FeaturedBadgePickerView()
    {
        InitializeComponent();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Cancel(); e.Handled = true; }
        };
        Scrim.PointerPressed += (_, e) => { Cancel(); e.Handled = true; };
    }

    private void Cancel() => (DataContext as FeaturedBadgePickerViewModel)?.CancelCommand.Execute(null);
}
