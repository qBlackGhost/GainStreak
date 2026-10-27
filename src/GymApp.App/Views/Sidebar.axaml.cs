using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GymApp.App.ViewModels;
using GymApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GymApp.App.Views;

/// <summary>The dark screens' navigation. <see cref="Current"/> is the highlighted item ("Dashboard", "Workouts",
/// "Splits", "Calories", "Map", "Inbox", "Friends"). The Inbox badge follows <see cref="InboxService.UnreadCount"/>. On Home, Dashboard raises <see cref="ScrollTopRequested"/> instead of navigating.</summary>
public partial class Sidebar : UserControl
{
    /// <summary>Below this window width the sidebar becomes a bar along the top.</summary>
    public const double NarrowBelow = 900;

    public static readonly StyledProperty<string?> CurrentProperty =
        AvaloniaProperty.Register<Sidebar, string?>(nameof(Current));

    public string? Current { get => GetValue(CurrentProperty); set => SetValue(CurrentProperty, value); }

    public event EventHandler? ScrollTopRequested;

    private Control? _host;

    public Sidebar()
    {
        InitializeComponent();
        if (Design.IsDesignMode) return;
        var user = GymApplication.Services.GetRequiredService<SessionService>().CurrentUser;
        var name = user?.Username ?? "";
        NameText.Text = name;
        InitialsText.Text = name.Length == 0 ? "?" : name[..Math.Min(2, name.Length)].ToUpperInvariant();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CurrentProperty)
        {
            NavDashboard.Classes.Set("current", Current == "Dashboard");
            NavWorkouts.Classes.Set("current", Current == "Workouts");
            NavSplits.Classes.Set("current", Current == "Splits");
            NavCalories.Classes.Set("current", Current == "Calories");
            NavGyms.Classes.Set("current", Current == "Map");
            NavInbox.Classes.Set("current", Current == "Inbox");
            NavFriends.Classes.Set("current", Current == "Friends");
        }
    }

    private InboxService? _inbox;

    private void ShowUnread()
    {
        var n = _inbox?.UnreadCount ?? 0;
        InboxBadge.IsVisible = n > 0;
        InboxText.Text = n.ToString();
    }

    // The window width decides the layout; the DockPanel holding the sidebar gets "narrow" too, for page margins
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _host = Parent as Control;
        if (_host is not null) _host.SizeChanged += OnHostSizeChanged;
        if (Design.IsDesignMode) return;
        _inbox = GymApplication.Services.GetRequiredService<InboxService>();
        _inbox.UnreadChanged += ShowUnread;
        ShowUnread();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_host is not null) _host.SizeChanged -= OnHostSizeChanged;
        _host = null;
        if (_inbox is not null) _inbox.UnreadChanged -= ShowUnread;
        _inbox = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnHostSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < NarrowBelow;
        Classes.Set("narrow", narrow);
        _host?.Classes.Set("narrow", narrow);
    }

    private static MainViewModel Shell => GymApplication.Services.GetRequiredService<MainViewModel>();
    private bool OnHome => Current == "Dashboard";

    private void OnDashboard(object? sender, RoutedEventArgs e)
    {
        if (OnHome) ScrollTopRequested?.Invoke(this, EventArgs.Empty);
        else Shell.ShowHome();
    }

    private void OnCalories(object? sender, RoutedEventArgs e) => Shell.ShowCalories();

    private void OnWorkouts(object? sender, RoutedEventArgs e) => Shell.ShowWorkouts();
    private void OnSplits(object? sender, RoutedEventArgs e) => Shell.ShowSplits();
    private void OnGyms(object? sender, RoutedEventArgs e) => Shell.ShowGyms();
    private void OnInbox(object? sender, RoutedEventArgs e) => Shell.ShowInbox();
    private void OnFriends(object? sender, RoutedEventArgs e) => Shell.ShowFriends();
    private void OnProfile(object? sender, RoutedEventArgs e) => Shell.ShowProfile();
    private void OnFeedback(object? sender, RoutedEventArgs e) => Shell.ShowFeedback();
    private async void OnLogout(object? sender, RoutedEventArgs e) => await GymApplication.Services.GetRequiredService<IAccountService>().LogOutAsync();
}
