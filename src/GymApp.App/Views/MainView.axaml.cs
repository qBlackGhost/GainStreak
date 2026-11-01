using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Animation;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GymApp.App.ViewModels;

namespace GymApp.App.Views;

/// <summary>The shell's view. Under <see cref="PhoneBelow"/> px wide it sets the "phone" class (the screens' phone
/// styles key on it) and tells <see cref="MainViewModel.IsPhone"/>. On the phones it also keeps the content clear of
/// the notch and the home indicator (the tab bar's background reaches behind it), moves it up out of the keyboard's way,
/// and sends the system back button to <see cref="MainViewModel.GoBack"/>.</summary>
public partial class MainView : UserControl
{
    /// <summary>Below this width the tab bar replaces the sidebar (rotation, split screen and a narrow desktop window
    /// switch live).</summary>
    public const double PhoneBelow = 700;

    private TopLevel? _top;
    private IInsetsManager? _insets;
    private IInputPane? _keyboard;
    /// <summary>When a field was last tapped or the keyboard last changed. Elapsed time on screen, so not AppClock.</summary>
    private long _keyboardMovedAt;
    private const int KeyboardSettleMs = 800;
    private MainViewModel? _vm;

    public MainView()
    {
        InitializeComponent();
        Tabs.SizeChanged += (_, _) => FitBodyAboveTabs();
        Tabs.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) FitBodyAboveTabs(); };
        SizeChanged += (_, e) =>
        {
            var phone = e.NewSize.Width < PhoneBelow;
            Classes.Set("phone", phone);
            if (_vm is not null) _vm.IsPhone = phone;
            // Again here: when Android makes a new activity for the app, the first safe area it reports is in device
            // pixels, before the window has its size, and the right one comes with no SafeAreaChanged of its own
            ApplyInsets();
        };
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnShellChanged;
            _vm = DataContext as MainViewModel;
            if (_vm is null) return;
            _vm.IsPhone = Classes.Contains("phone");
            _vm.PropertyChanged += OnShellChanged;
            ShowScreen(_vm.Current);
        };
    }

    private static bool OnPhone => GymApplication.Platform is PhonePlatform;

    /// <summary>Whether <paramref name="view"/> is laid out for a phone: under a MainView with the "phone" class. For
    /// the few screens that move a part around in code rather than with styles.</summary>
    public static bool IsPhoneLayout(Visual view) => view.FindAncestorOfType<MainView>()?.Classes.Contains("phone") == true;

    public static void HideKeyboard(Visual view)
    {
        // With no text field focused, the system closes the keyboard
        TopLevel.GetTopLevel(view)?.FocusManager?.Focus(null);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!OnPhone || TopLevel.GetTopLevel(this) is not { } top) return;
        _top = top;
        _top.BackRequested += OnBackRequested;
        _insets = top.InsetsManager;
        if (_insets is not null)
        {
            _insets.DisplayEdgeToEdgePreference = true;
            _insets.SafeAreaChanged += OnSafeAreaChanged;
        }
        _keyboard = top.InputPane;
        if (_keyboard is not null) _keyboard.StateChanged += OnKeyboardChanged;
        AddHandler(TappedEvent, OnTapped, handledEventsToo: true);
        ActualThemeVariantChanged += OnThemeChanged;
        OnThemeChanged(this, EventArgs.Empty);
        ApplyInsets();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_top is not null) _top.BackRequested -= OnBackRequested;
        if (_insets is not null) _insets.SafeAreaChanged -= OnSafeAreaChanged;
        if (_keyboard is not null) _keyboard.StateChanged -= OnKeyboardChanged;
        RemoveHandler(TappedEvent, OnTapped);
        ActualThemeVariantChanged -= OnThemeChanged;
        _top = null;
        _insets = null;
        _keyboard = null;
        base.OnDetachedFromVisualTree(e);
    }

    // Android's back button: a popup closes, then the previous screen; at a tab root Android leaves the app
    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (_vm?.GoBack() == true) e.Handled = true;
    }

    /// <summary>Screens that are one place on the phones, switched by a pill at the top (the GainStreak Mobile
    /// canvas): Log in and Create account, the Friends tab's Leaderboard and Map. Between the two the screen fades.</summary>
    private static readonly Type[][] Pairs =
    [
        [typeof(LoginViewModel), typeof(RegisterViewModel)],
        [typeof(FriendsViewModel), typeof(GymsViewModel)],
    ];

    private static readonly IPageTransition Fade = new FadeIn(TimeSpan.FromMilliseconds(220));

    // Every other change is a cut (a fade of no length), which keeps both of the control's presenters opaque
    private static readonly IPageTransition Cut = new FadeIn(TimeSpan.Zero);

    private void ShowScreen(ViewModelBase? screen)
    {
        var from = Screen.Content?.GetType();
        var to = screen?.GetType();
        var paired = Classes.Contains("phone") && from is not null && to is not null && from != to
                     && Array.Exists(Pairs, p => Array.IndexOf(p, from) >= 0 && Array.IndexOf(p, to) >= 0);
        Screen.PageTransition = paired ? Fade : Cut;
        Screen.Content = screen;
    }

    private void OnSafeAreaChanged(object? sender, SafeAreaChangedArgs e) => ApplyInsets();

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Current)) ShowScreen(_vm?.Current);
        if (e.PropertyName == nameof(MainViewModel.ShowsBars)) ApplyInsets();
    }

    // The page colour behind the status bar and the home indicator too, so the status bar's icons are dark on the
    // light theme and light on the dark one. The theme dictionaries need the current theme to look it up
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        Background = this.TryFindResource("PageBg", ActualThemeVariant, out var brush) ? brush as IBrush : null;
        if (_insets is not null && Background is ISolidColorBrush page) _insets.SystemBarColor = page.Color;
    }

    /// <summary>A tap outside a field closes the keyboard. Not one on a button: the show-password eye keeps it open, and
    /// any other button does its own thing. Nor one just after a field was tapped or the keyboard opened: the page is
    /// still moving, so a tap aimed at the eye can land beside it, and closing would throw the page back.</summary>
    private void OnTapped(object? sender, TappedEventArgs e)
    {
        var tapped = e.Source as Visual;
        if (tapped?.FindAncestorOfType<TextBox>(includeSelf: true) is not null)
        {
            _keyboardMovedAt = Environment.TickCount64;
            return;
        }
        if (_top?.FocusManager?.GetFocusedElement() is not TextBox) return;
        if (Environment.TickCount64 - _keyboardMovedAt < KeyboardSettleMs) return;
        if (tapped?.FindAncestorOfType<Button>(includeSelf: true) is null) HideKeyboard(this);
    }

    private void OnKeyboardChanged(object? sender, InputPaneStateEventArgs e)
    {
        ApplyInsets();
        _keyboardMovedAt = Environment.TickCount64;
        // Once the content has moved up, scroll the field being typed into back into view
        if (e.NewState == InputPaneState.Open)
            Dispatcher.UIThread.Post(() => (_top?.FocusManager?.GetFocusedElement() as Control)?.BringIntoView(),
                DispatcherPriority.Loaded);
    }

    /// <summary>Safe areas on all four sides. At the bottom: the keyboard while it's up (the tab bar hides), else the tab
    /// bar takes the home indicator's inset inside its own background, else the page does.</summary>
    private void ApplyInsets()
    {
        if (_top is null) return;
        var safe = _insets?.SafeAreaPadding ?? default;
        var keyboard = KeyboardOverlap();
        var tabs = _vm?.ShowsBars == true;
        TabHost.IsVisible = keyboard == 0;
        Tabs.Padding = new Thickness(0, 0, 0, safe.Bottom);
        Padding = new Thickness(safe.Left, safe.Top, safe.Right, keyboard > 0 ? keyboard : tabs ? 0 : safe.Bottom);
        FitBodyAboveTabs();
    }

    /// <summary>The tab bar lies over the bottom of the screen (so the raised Home circle overlaps it); the screen
    /// itself stops at the bar's top edge.</summary>
    private void FitBodyAboveTabs()
    {
        var bar = TabHost.IsVisible && Tabs.IsVisible ? Math.Max(0, Tabs.Bounds.Height - TabBar.RiseHeight) : 0;
        Body.Margin = new Thickness(0, 0, 0, bar);
    }

    /// <summary>How much of this view the on-screen keyboard covers. Zero when the system has already made the
    /// window shorter for it.</summary>
    private double KeyboardOverlap()
    {
        if (_top is null || _keyboard is not { State: InputPaneState.Open } k || k.OccludedRect.Height <= 0) return 0;
        return Math.Max(0, _top.ClientSize.Height - k.OccludedRect.Top);
    }
}
