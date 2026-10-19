using Avalonia.Controls;
using Avalonia.Input;
using GymApp.Desktop.ViewModels;

namespace GymApp.Desktop.Views;

public partial class FriendsView : UserControl
{
    /// <summary>Below this content width the requests column goes under the leaderboard.</summary>
    private const double TwoColumnMinWidth = 880;

    public FriendsView()
    {
        InitializeComponent();
        Columns.SizeChanged += (_, e) => Columns.Classes.Set("stack", e.NewSize.Width < TwoColumnMinWidth);

        // Enter searches, like the button
        SearchBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && DataContext is FriendsViewModel vm && vm.SearchCommand.CanExecute(null))
                vm.SearchCommand.Execute(null);
        };
    }
}
