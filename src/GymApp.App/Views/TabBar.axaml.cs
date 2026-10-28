using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using GymApp.App.ViewModels;

namespace GymApp.App.Views;

/// <summary>The phones' tab bar. A tab opens its screen, which clears the back stack, so tapping the current tab goes
/// back to its start.</summary>
public partial class TabBar : UserControl
{
    /// <summary>True when the selected tab is the parameter.</summary>
    public static readonly IValueConverter Is =
        new FuncValueConverter<AppTab?, object?, bool>((tab, parameter) => parameter is AppTab t && tab == t);

    /// <summary>The transparent strip above the bar that the Home circle rises into (the first row in TabBar.axaml).</summary>
    public const double RiseHeight = 12;

    public TabBar()
    {
        InitializeComponent();
    }

    private MainViewModel? Shell => DataContext as MainViewModel;

    private void OnSplits(object? sender, RoutedEventArgs e) => Shell?.ShowSplits();
    private void OnWorkouts(object? sender, RoutedEventArgs e) => Shell?.ShowWorkouts();
    private void OnHome(object? sender, RoutedEventArgs e) => Shell?.ShowHome();
    private void OnCalories(object? sender, RoutedEventArgs e) => Shell?.ShowCalories();
    private void OnFriends(object? sender, RoutedEventArgs e) => Shell?.ShowFriends();
}
