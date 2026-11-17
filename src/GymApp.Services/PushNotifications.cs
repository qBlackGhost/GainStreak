using System;
using System.Threading.Tasks;
using GymApp.Contracts;

namespace GymApp.Services;

public enum PushPermission { NotAsked, Allowed, Denied }

/// <summary>This phone's push token, as the server stores it.</summary>
public sealed record PushToken(PushPlatform Platform, string Token, bool Sandbox);

/// <summary>
/// The phone's side of push notifications (Phase 22), from the head: APNs on iOS, Firebase on Android. The desktop, the
/// tests and an Android build without Firebase have <see cref="NoPushNotifications"/>. In GymApp.Services, not GymApp.App,
/// because <see cref="RemotePushRegistration"/> needs it.
/// </summary>
public interface IPushNotifications
{
    /// <summary>False on the desktop and on a phone build that can't receive pushes: Profile and Friends hide them.</summary>
    bool IsSupported { get; }

    /// <summary>Whether the phone lets the app notify. The same permission as Reminders (16g).</summary>
    Task<PushPermission> GetPermissionAsync();

    /// <summary>Asks for the permission when it hasn't been asked (iOS asks only once). True if allowed.</summary>
    Task<bool> RequestPermissionAsync();

    /// <summary>This phone's token, registering with APNs or FCM first if needed; null when there's none (yet).</summary>
    Task<PushToken?> GetTokenAsync();

    /// <summary>The token changed, or arrived for the first time. May be raised on any thread.</summary>
    event Action? TokenChanged;
}

public sealed class NoPushNotifications : IPushNotifications
{
    public static readonly NoPushNotifications Instance = new();

    public bool IsSupported => false;
    public Task<PushPermission> GetPermissionAsync() => Task.FromResult(PushPermission.Denied);
    public Task<bool> RequestPermissionAsync() => Task.FromResult(false);
    public Task<PushToken?> GetTokenAsync() => Task.FromResult<PushToken?>(null);
    public event Action? TokenChanged { add { } remove { } }
}
