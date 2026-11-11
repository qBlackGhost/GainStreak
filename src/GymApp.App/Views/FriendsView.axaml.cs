using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Media;
using GymApp.App.ViewModels;

namespace GymApp.App.Views;

public partial class FriendsView : UserControl
{
    /// <summary>The invite card's icon, from its resource key (IcCheck, IcWifiOff…).</summary>
    public static readonly IValueConverter IconFor = new FuncValueConverter<string?, Geometry?>(key =>
        key is not null && Application.Current?.TryFindResource(key, out var g) == true ? g as Geometry : null);

    /// <summary>Below this content width the requests column goes under the leaderboard.</summary>
    private const double TwoColumnMinWidth = 880;

    public FriendsView()
    {
        InitializeComponent();
        Columns.SizeChanged += (_, e) =>
        {
            Columns.Classes.Set("stack", e.NewSize.Width < TwoColumnMinWidth);
            PlaceYourFriends(MainView.IsPhoneLayout(this));
        };

        // Enter searches, like the button
        SearchBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && DataContext is FriendsViewModel vm && vm.SearchCommand.CanExecute(null))
                vm.SearchCommand.Execute(null);
        };
    }

    /// <summary>Phones (the GainStreak Mobile canvas): the leaderboard, then adding friends, then your friends, then
    /// anyone blocked. Wider:
    /// your friends under the leaderboard, adding friends beside them.</summary>
    private void PlaceYourFriends(bool phone)
    {
        if (phone && YourFriends.Parent == LeftColumn)
        {
            LeftColumn.Children.Remove(YourFriends);
            RightColumn.Children.Insert(1, YourFriends);
        }
        else if (!phone && YourFriends.Parent == RightColumn)
        {
            RightColumn.Children.Remove(YourFriends);
            LeftColumn.Children.Add(YourFriends);
        }
    }
}
