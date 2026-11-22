using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GymApp.App.ViewModels;

namespace GymApp.App.Views;

/// <summary>The Add food sheet. Escape and a tap on the dimmed page close it.</summary>
public partial class AddFoodView : UserControl
{
    public AddFoodView()
    {
        InitializeComponent();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
        Scrim.PointerPressed += (_, e) =>
        {
            if (e.Source == Scrim) { Close(); e.Handled = true; }
        };
    }

    private void Close() => (DataContext as AddFoodViewModel)?.CloseCommand.Execute(null);

    // The unit pill's buttons: the tapped option's position becomes the selection
    private void PickUnit(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AmountOptionViewModel option } &&
            DataContext is AddFoodViewModel { Amount: { } amount })
        {
            var i = 0;
            foreach (var o in amount.Options) { if (ReferenceEquals(o, option)) break; i++; }
            amount.SelectedIndex = i;
        }
    }
}
