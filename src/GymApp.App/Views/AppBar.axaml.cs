using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GymApp.App.ViewModels;
using GymApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GymApp.App.Views;

/// <summary>The phones' app bar. The inbox badge follows <see cref="InboxService.UnreadCount"/> while it shows.</summary>
public partial class AppBar : UserControl
{
    private InboxService? _inbox;

    public AppBar()
    {
        InitializeComponent();
    }

    private MainViewModel? Shell => DataContext as MainViewModel;

    private void OnHome(object? sender, RoutedEventArgs e) => Shell?.ShowHome();
    private void OnInbox(object? sender, RoutedEventArgs e) => Shell?.ShowInbox();
    private void OnProfile(object? sender, RoutedEventArgs e) => Shell?.ShowProfile();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Design.IsDesignMode) return;
        _inbox = GymApplication.Services.GetRequiredService<InboxService>();
        _inbox.UnreadChanged += ShowUnread;
        ShowUnread();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_inbox is not null) _inbox.UnreadChanged -= ShowUnread;
        _inbox = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void ShowUnread()
    {
        var n = _inbox?.UnreadCount ?? 0;
        InboxBadge.IsVisible = n > 0;
        InboxText.Text = n.ToString();
        AutomationProperties.SetName(InboxButton, n > 0 ? $"Inbox, {n} new" : "Inbox");
    }
}
