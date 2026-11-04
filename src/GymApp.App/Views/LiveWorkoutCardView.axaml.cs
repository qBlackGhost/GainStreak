using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GymApp.App.ViewModels;

namespace GymApp.App.Views;

public partial class LiveWorkoutCardView : UserControl
{
    public LiveWorkoutCardView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    // The keyboard's Next key goes from the weight to the reps, and from the reps it saves the set, so a set is one
    // thumb's work
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not LiveWorkoutCardViewModel card) return;
        if (ReferenceEquals(e.Source, WeightBox)) { RepsBox.Focus(NavigationMethod.Tab); e.Handled = true; }
        else if (ReferenceEquals(e.Source, RepsBox))
        {
            if (card.IsEditing ? card.CanSave : card.CanNext)
                (card.IsEditing ? card.SaveEditCommand : card.NextCommand).Execute(null);
            e.Handled = true;
        }
    }
}
