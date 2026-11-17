using Foundation;
using GymApp.Contracts;
using GymApp.Services;
using ObjCRuntime;
using UIKit;
using UserNotifications;

namespace GymApp.iOS;

/// <summary>
/// Friend notifications through Apple's push service (Phase 22). The app registers with APNs at every start, whatever
/// the permission (iOS hands out a token either way; alerts only show once allowed), so the token is there as soon as
/// friend notifications are turned on. <see cref="AppDelegate"/> passes the token in. Debug builds and the Simulator
/// get APNs' sandbox tokens; TestFlight and App Store builds production ones.
/// </summary>
public sealed class IosPushNotifications : IPushNotifications
{
    /// <summary>How long <see cref="GetTokenAsync"/> waits for APNs' answer before trying again later.</summary>
    private static readonly TimeSpan TokenWait = TimeSpan.FromSeconds(10);

    private static readonly Lock Gate = new();
    private static string? _token;
    private static TaskCompletionSource<string?> _tokenArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static event Action? Changed;

    /// <summary>A Debug build, or any build on the Simulator: APNs' sandbox.</summary>
    public static bool Sandbox
    {
        get
        {
#if DEBUG
            return true;
#else
            return Runtime.Arch == Arch.SIMULATOR;
#endif
        }
    }

    public bool IsSupported => true;

    public event Action? TokenChanged
    {
        add => Changed += value;
        remove => Changed -= value;
    }

    /// <summary>Asks APNs for a token (again). On the main thread; the answer comes to <see cref="AppDelegate"/>.</summary>
    public static void Register() =>
        UIApplication.SharedApplication.InvokeOnMainThread(() => UIApplication.SharedApplication.RegisterForRemoteNotifications());

    /// <summary>APNs' token, from <c>application:didRegisterForRemoteNotificationsWithDeviceToken:</c>.</summary>
    public static void OnToken(NSData deviceToken)
    {
        var token = Convert.ToHexStringLower(deviceToken.ToArray());
        bool changed;
        TaskCompletionSource<string?> waiting;
        lock (Gate)
        {
            changed = token != _token;
            _token = token;
            waiting = _tokenArrived;
        }
        waiting.TrySetResult(token);
        if (changed) Changed?.Invoke();
    }

    /// <summary>No token this time (no network, or a build without the push entitlement).</summary>
    public static void OnFailed(NSError error)
    {
        TaskCompletionSource<string?> waiting;
        lock (Gate)
        {
            waiting = _tokenArrived;
            _tokenArrived = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        waiting.TrySetResult(null);
        Console.WriteLine($"Push: no APNs token: {error.LocalizedDescription}");
    }

    public async Task<PushPermission> GetPermissionAsync()
    {
        var settings = await UNUserNotificationCenter.Current.GetNotificationSettingsAsync();
        return settings.AuthorizationStatus switch
        {
            UNAuthorizationStatus.NotDetermined => PushPermission.NotAsked,
            UNAuthorizationStatus.Denied => PushPermission.Denied,
            _ => PushPermission.Allowed   // authorized, provisional, ephemeral
        };
    }

    /// <summary>The same prompt as Reminders (16g): iOS shows it only once.</summary>
    public async Task<bool> RequestPermissionAsync()
    {
        var (granted, _) = await UNUserNotificationCenter.Current.RequestAuthorizationAsync(
            UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound);
        if (granted) Register();
        return granted;
    }

    public async Task<PushToken?> GetTokenAsync()
    {
        Task<string?> arriving;
        lock (Gate)
        {
            if (_token is { } known) return new PushToken(PushPlatform.Apns, known, Sandbox);
            arriving = _tokenArrived.Task;
        }
        Register();
        var token = await arriving.WaitAsync(TokenWait).ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : null);
        return token is null ? null : new PushToken(PushPlatform.Apns, token, Sandbox);
    }
}
