using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using GymApp.App.ViewModels;
using GymApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GymApp.App.Views;

/// <summary>The phones' tab bar. A tab opens its root screen, which clears the back stack, so tapping the current tab
/// goes back to its start. The More tab gets a dot while the inbox has something new.</summary>
public partial class TabBar : UserControl
{
    /// <summary>True when the selected tab is the parameter.</summary>
    public static readonly IValueConverter Is =
        new FuncValueConverter<AppTab?, object?, bool>((tab, parameter) => parameter is AppTab t && tab == t);

    private InboxService? _inbox;

    public TabBar()
    {
        InitializeComponent();
    }

    private MainViewModel? Shell => DataContext as MainViewModel;

    private void OnHome(object? sender, RoutedEventArgs e) => Shell?.ShowHome();
    private void OnWorkouts(object? sender, RoutedEventArgs e) => Shell?.ShowWorkouts();
    private void OnCalories(object? sender, RoutedEventArgs e) => Shell?.ShowCalories();
    private void OnFriends(object? sender, RoutedEventArgs e) => Shell?.ShowFriends();
    private void OnMore(object? sender, RoutedEventArgs e) => Shell?.ShowMore();

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Design.IsDesignMode) return;
        _inbox = GymApplication.Services.GetRequiredService<InboxService>();
        _inbox.UnreadChanged += ShowUnread;
        ShowUnread();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_inbox is not null) _inbox.UnreadChanged -= ShowUnread;
        _inbox = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void ShowUnread() => InboxBadge.IsVisible = _inbox?.UnreadCount > 0;
}
