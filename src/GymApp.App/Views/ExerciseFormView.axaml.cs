using Avalonia.Controls;
using Avalonia.Input;
using GymApp.App.ViewModels;

namespace GymApp.App.Views;

public partial class ExerciseFormView : UserControl
{
    public ExerciseFormView()
    {
        InitializeComponent();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Cancel(); e.Handled = true; }
        };
        Scrim.PointerPressed += (_, e) => { Cancel(); e.Handled = true; };
        // The cursor in the name box, but no keyboard jumping over a phone's sheet
        Loaded += (_, _) => { if (!MainView.IsPhoneLayout(this)) NameBox.Focus(NavigationMethod.Tab); };
    }

    private void Cancel() => (DataContext as ExerciseFormViewModel)?.CancelCommand.Execute(null);
}
