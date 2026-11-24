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
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        _vm = DataContext as HomeViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnVmChanged;
        PlaceTodayCard();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HomeViewModel.ShowDoneRow)) PlaceTodayCard();
    }

    /// <summary>Phones, once today's workout is finished (0.27.1): Today's workout moves under Recent PRs, just above
    /// Friends; otherwise it leads the cards.</summary>
    private void PlaceTodayCard()
    {
        var done = _vm?.ShowDoneRow == true;
        var children = Cards.Children;
        var at = children.IndexOf(TodayCard);
        var target = done ? children.IndexOf(PrsCard) : 0;
        if (at < 0 || target < 0) return;
        if (done && at > target) return;
        if (!done && at == 0) return;
        children.RemoveAt(at);
        children.Insert(done ? children.IndexOf(PrsCard) + 1 : 0, TodayCard);
    }

    /// <summary>Two equal columns of cards when there's room, otherwise one full-width column.</summary>
    private void LayOutCards(double width)
    {
        if (width <= 0) return;
        var itemWidth = width >= TwoColumnMinWidth ? (width - Cards.ItemSpacing) / 2 : width;
        if (double.IsNaN(Cards.ItemWidth) || System.Math.Abs(Cards.ItemWidth - itemWidth) > 0.5)
            Cards.ItemWidth = System.Math.Floor(itemWidth);
    }

    // Dashboard is this screen, so its nav item scrolls to the top instead of navigating
    private void OnScrollTop(object? sender, System.EventArgs e) => MainScroll.Offset = new Vector(0, 0);
}
