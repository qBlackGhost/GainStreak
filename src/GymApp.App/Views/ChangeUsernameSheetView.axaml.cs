using Avalonia.Controls;
using Avalonia.Input;
using GymApp.App.ViewModels;

namespace GymApp.App.Views;

/// <summary>The Change username sheet. Escape and a tap on the dimmed page close it.</summary>
public partial class ChangeUsernameSheetView : UserControl
{
    public ChangeUsernameSheetView()
    {
        InitializeComponent();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        Scrim.PointerPressed += (_, e) => { if (e.Source == Scrim) { Close(); e.Handled = true; } };
    }

    private void Close() => (DataContext as ChangeUsernameSheetViewModel)?.CloseCommand.Execute(null);
}
