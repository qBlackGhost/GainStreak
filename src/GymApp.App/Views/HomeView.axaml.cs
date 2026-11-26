using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GymApp.App.ViewModels;

namespace GymApp.App.Views;

public partial class HomeView : UserControl
{
    /// <summary>Below this content width the header and the streak hero stack.</summary>
    private const double CompactMaxWidth = 760;

    /// <summary>Below this width the dashboard cards drop to one column.</summary>
    private const double TwoColumnMinWidth = 860;

    private HomeViewModel? _vm;

    public HomeView()
    {
        InitializeComponent();
        Main.SizeChanged += (_, e) => Main.Classes.Set("compact", e.NewSize.Width < CompactMaxWidth);
        Cards.SizeChanged += (_, e) => LayOutCards(e.NewSize.Width);
    }

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmChanged;
            _vm.RecentPrs.PropertyChanged -= OnPrsChanged;
        }
        _vm = DataContext as HomeViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmChanged;
            _vm.RecentPrs.PropertyChanged += OnPrsChanged;
        }
        PlaceTodayCard();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HomeViewModel.ShowDoneRow) or nameof(HomeViewModel.HasFoodLog)) PlaceTodayCard();
    }

    private bool _twoColumns;

    private void OnPrsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RecentPrsCardViewModel.HasAny) && _twoColumns) PlaceTodayCard();
    }

    /// <summary>Phones, once today's workout is finished (0.27.1): Today's workout moves under Recent PRs, just above
    /// Friends; otherwise it leads the cards. In two columns (desktop): Calories and Friends side by side on top, then
    /// Recent PRs and Today's workout. The calories card that doesn't show goes last: the WrapPanel gives even a hidden
    /// child a column, which would leave a hole.</summary>
    private void PlaceTodayCard()
    {
        var children = Cards.Children;
        if (_twoColumns)
        {
            var calories = _vm?.HasFoodLog == true ? FoodCard : CaloriesCard;
            var hidden = calories == FoodCard ? CaloriesCard : FoodCard;
            // Without PRs, Today's workout takes their place on the left and the empty PRs card goes last too
            if (_vm?.RecentPrs.HasAny == false) Order(calories, FriendsCard, TodayCard, hidden, PrsCard);
            else Order(calories, FriendsCard, PrsCard, TodayCard, hidden);
            return;
        }

        var done = _vm?.ShowDoneRow == true;
        Order(done ? [CaloriesCard, FoodCard, PrsCard, TodayCard, FriendsCard] : [TodayCard, CaloriesCard, FoodCard, PrsCard, FriendsCard]);
    }

    private void Order(params Control[] cards)
    {
        var children = Cards.Children;
        for (var i = 0; i < cards.Length; i++)
        {
            var at = children.IndexOf(cards[i]);
            if (at < 0 || at == i) continue;
            children.RemoveAt(at);
            children.Insert(i, cards[i]);
        }
    }

    /// <summary>Two equal columns of cards when there's room, otherwise one full-width column.</summary>
    private void LayOutCards(double width)
    {
        if (width <= 0) return;
        var two = width >= TwoColumnMinWidth;
        if (two != _twoColumns) { _twoColumns = two; PlaceTodayCard(); }
        var itemWidth = two ? (width - Cards.ItemSpacing) / 2 : width;
        if (double.IsNaN(Cards.ItemWidth) || System.Math.Abs(Cards.ItemWidth - itemWidth) > 0.5)
            Cards.ItemWidth = System.Math.Floor(itemWidth);
    }

    // Dashboard is this screen, so its nav item scrolls to the top instead of navigating
    private void OnScrollTop(object? sender, System.EventArgs e) => MainScroll.Offset = new Vector(0, 0);
}
