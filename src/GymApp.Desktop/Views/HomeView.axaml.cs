using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace GymApp.Desktop.Views;

public partial class HomeView : UserControl
{
    /// <summary>Below this window width the sidebar becomes a bar along the top.</summary>
    private const double SidebarMinWidth = 900;

    /// <summary>Below this content width the header and the streak hero stack.</summary>
    private const double CompactMaxWidth = 760;

    /// <summary>Below this width the dashboard cards drop to one column.</summary>
    private const double TwoColumnMinWidth = 860;

    public HomeView()
    {
        InitializeComponent();
        Root.SizeChanged += (_, e) => Root.Classes.Set("narrow", e.NewSize.Width < SidebarMinWidth);
        Main.SizeChanged += (_, e) => Main.Classes.Set("compact", e.NewSize.Width < CompactMaxWidth);
        Cards.SizeChanged += (_, e) => LayOutCards(e.NewSize.Width);
    }

    /// <summary>Two equal columns of cards when there's room, otherwise one full-width column.</summary>
    private void LayOutCards(double width)
    {
        if (width <= 0) return;
        var itemWidth = width >= TwoColumnMinWidth ? (width - Cards.ItemSpacing) / 2 : width;
        if (double.IsNaN(Cards.ItemWidth) || System.Math.Abs(Cards.ItemWidth - itemWidth) > 0.5)
            Cards.ItemWidth = System.Math.Floor(itemWidth);
    }

    // Dashboard and Calories are on this screen, so their nav items scroll instead of navigating
    private void OnDashboardClick(object? sender, RoutedEventArgs e) => MainScroll.Offset = new Vector(0, 0);
    private void OnCaloriesClick(object? sender, RoutedEventArgs e) => CaloriesCard.BringIntoView();
}
