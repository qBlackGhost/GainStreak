using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GymApp.Desktop.Views;

public partial class HomeView : UserControl
{
    /// <summary>Below this width the dashboard drops to one column.</summary>
    private const double TwoColumnMinWidth = 700;

    public HomeView()
    {
        InitializeComponent();
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
}
