using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Services;

namespace GymApp.App.ViewModels;

/// <summary>Profile → Notifications → Friend requests (Phase 22), on phones that can receive pushes. Turning it on
/// asks for permission if needed; refused, the switch goes back off with the note. Each change reaches the server at
/// once through <see cref="IPushRegistration"/>.</summary>
public partial class FriendNotificationsViewModel(IPushRegistration registration, IPushNotifications device) : ObservableObject
{
    public const string RefusedText =
        "Notifications are off for GainStreak. Allow them in your phone's Settings, then turn this on again.";

    private bool _loading;

    /// <summary>False on the desktop and on a phone build without push: the row is hidden.</summary>
    public bool IsSupported => device.IsSupported;

    [ObservableProperty] public partial bool IsOn { get; set; }

    /// <summary>Why it's off although asked for, or null.</summary>
    [ObservableProperty] public partial string? Note { get; set; }

    public async Task LoadAsync()
    {
        if (!IsSupported) return;
        var state = await registration.GetStateAsync();
        Set(state.Enabled == true && state.Permission == PushPermission.Allowed);
        // On, but the phone's Settings took the permission away since
        Note = state.Enabled == true && state.Permission == PushPermission.Denied ? RefusedText : null;
    }

    partial void OnIsOnChanged(bool value)
    {
        if (!_loading) _ = SaveAsync(value);
    }

    private async Task SaveAsync(bool on)
    {
        Note = null;
        if (await registration.SetEnabledAsync(on) != PushSwitchResult.Refused) return;
        Set(false);
        Note = RefusedText;
    }

    private void Set(bool on)
    {
        _loading = true;
        IsOn = on;
        _loading = false;
    }
}

/// <summary>
/// Friends' card asking to turn friend notifications on (Phase 22, the canvas's Friends · Turn on notifications). Shown
/// only while nothing is decided: push works on this phone, the switch was never set, the phone doesn't already allow
/// notifications (then they're on by themselves), and "Not now" wasn't tapped. Friends asks it to consider showing
/// once the user has friends or requests, opens the QR sheet, or sends a request.
/// </summary>
public partial class FriendPushCardViewModel(IPushRegistration registration) : ObservableObject
{
    public const string TurnedOnText = "Friend notifications are on. You can turn them off in Profile.";
    public const string RefusedText =
        "Notifications are off for GainStreak. Allow them in your phone's Settings, then turn them on in Profile.";

    [ObservableProperty] public partial bool IsVisible { get; private set; }

    /// <summary>"Know when friends add you", with Not now and Turn on.</summary>
    [ObservableProperty] public partial bool IsAsk { get; private set; }

    /// <summary>The answer, with one button.</summary>
    [ObservableProperty] public partial bool IsMessage { get; private set; }
    [ObservableProperty] public partial string? Message { get; private set; }
    [ObservableProperty] public partial bool IsGood { get; private set; }
    /// <summary>The message's icon: a resource key (IcCheck, IcBellOff).</summary>
    [ObservableProperty] public partial string Icon { get; private set; } = "IcCheck";
    [ObservableProperty] public partial string OkLabel { get; private set; } = "OK";

    /// <summary>Shows the question if nothing is decided yet. Never hides a card already showing.</summary>
    public async Task ConsiderAsync()
    {
        if (IsVisible) return;
        var s = await registration.GetStateAsync();
        if (!s.IsSupported || s.Enabled is not null || s.CardDismissed || s.Permission == PushPermission.Allowed) return;
        IsMessage = false;
        IsAsk = true;
        IsVisible = true;
    }

    [RelayCommand]
    private async Task TurnOnAsync()
    {
        var result = await registration.SetEnabledAsync(true);
        if (result == PushSwitchResult.Off) { Hide(); return; }
        var on = result == PushSwitchResult.On;
        Message = on ? TurnedOnText : RefusedText;
        IsGood = on;
        Icon = on ? "IcCheck" : "IcBellOff";
        OkLabel = on ? "Done" : "OK";
        IsAsk = false;
        IsMessage = true;
    }

    [RelayCommand]
    private async Task NotNowAsync()
    {
        Hide();
        await registration.DismissCardAsync();
    }

    [RelayCommand]
    private void Close() => Hide();

    private void Hide()
    {
        IsVisible = false;
        IsAsk = false;
        IsMessage = false;
    }
}
