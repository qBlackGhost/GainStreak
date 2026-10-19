using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GymApp.Desktop.ViewModels;
using GymApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GymApp.Desktop.Views;

/// <summary>The dark screens' navigation. <see cref="Current"/> is the highlighted item ("Dashboard", "Workouts",
/// "Splits", "Calories", "Friends"). On Home, Dashboard raises <see cref="ScrollTopRequested"/> instead of navigating.</summary>
public partial class Sidebar : UserControl
{
    /// <summary>Below this window width the sidebar becomes a bar along the top.</summary>
    public const double NarrowBelow = 900;

    public static readonly StyledProperty<string?> CurrentProperty =
        AvaloniaProperty.Register<Sidebar, string?>(nameof(Current));

    public static readonly StyledProperty<int> FriendRequestsProperty =
        AvaloniaProperty.Register<Sidebar, int>(nameof(FriendRequests));

    public string? Current { get => GetValue(CurrentProperty); set => SetValue(CurrentProperty, value); }
    public int FriendRequests { get => GetValue(FriendRequestsProperty); set => SetValue(FriendRequestsProperty, value); }

    public event EventHandler? ScrollTopRequested;

    private Control? _host;

    public Sidebar()
    {
        InitializeComponent();
        if (Design.IsDesignMode) return;
        var user = App.Services.GetRequiredService<SessionService>().CurrentUser;
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
            NavFriends.Classes.Set("current", Current == "Friends");
        }
        else if (change.Property == FriendRequestsProperty)
        {
            RequestsBadge.IsVisible = FriendRequests > 0;
            RequestsText.Text = FriendRequests.ToString();
        }
    }

    // The window width decides the layout; the DockPanel holding the sidebar gets "narrow" too, for page margins
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _host = Parent as Control;
        if (_host is not null) _host.SizeChanged += OnHostSizeChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_host is not null) _host.SizeChanged -= OnHostSizeChanged;
        _host = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnHostSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < NarrowBelow;
        Classes.Set("narrow", narrow);
        _host?.Classes.Set("narrow", narrow);
    }

    private static MainViewModel Shell => App.Services.GetRequiredService<MainViewModel>();
    private bool OnHome => Current == "Dashboard";

    private void OnDashboard(object? sender, RoutedEventArgs e)
    {
        if (OnHome) ScrollTopRequested?.Invoke(this, EventArgs.Empty);
        else Shell.ShowHome();
    }

    private void OnCalories(object? sender, RoutedEventArgs e) => Shell.ShowCalories();

    private void OnWorkouts(object? sender, RoutedEventArgs e) => Shell.ShowWorkouts();
    private void OnSplits(object? sender, RoutedEventArgs e) => Shell.ShowSplits();
    private void OnGyms(object? sender, RoutedEventArgs e) => Shell.ShowGymSummary();
    private void OnFriends(object? sender, RoutedEventArgs e) => Shell.ShowFriends();
    private void OnProfile(object? sender, RoutedEventArgs e) => Shell.ShowProfile();
    private void OnLogout(object? sender, RoutedEventArgs e) => App.Services.GetRequiredService<SessionService>().SignOut();
}
