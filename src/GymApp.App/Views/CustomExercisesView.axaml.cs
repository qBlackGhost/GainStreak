using Avalonia.Controls;
using Avalonia.Input;
using GymApp.App.ViewModels;

namespace GymApp.App.Views;

public partial class CustomExercisesView : UserControl
{
    public CustomExercisesView() => InitializeComponent();

    private void OnDeleteScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is CustomExercisesViewModel vm) vm.IsConfirmingDelete = false;
        e.Handled = true;
    }
}
