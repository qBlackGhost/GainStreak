using Avalonia;
using Avalonia.iOS;
using Foundation;
using UIKit;
using UserNotifications;
using GymApp.App;

namespace GymApp.iOS;

/// <summary>Starts the shared app (<see cref="GymApplication"/>) with the iOS platform.</summary>
[Register("AppDelegate")]
public partial class AppDelegate : AvaloniaAppDelegate<GymApplication>
{
    private readonly NotificationDelegate _notifications = new();

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        PhonePlatform.UseDebugServer(BuiltSettings.DebugServerUrl);
        GymApplication.Platform = new IosPlatform();
        // Before launching ends, so a tap that started the app is delivered too (16g, and pushes from Phase 22)
        UNUserNotificationCenter.Current.Delegate = _notifications;
        IosPushNotifications.Register();   // a token whatever the permission, ready when friend notifications go on
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }

    /// <summary>APNs' token for this install (Phase 22). Avalonia's delegate doesn't handle it, so it's exported here.</summary>
    [Export("application:didRegisterForRemoteNotificationsWithDeviceToken:")]
    public void RegisteredForRemoteNotifications(UIApplication application, NSData deviceToken) =>
        IosPushNotifications.OnToken(deviceToken);

    [Export("application:didFailToRegisterForRemoteNotificationsWithError:")]
    public void FailedToRegisterForRemoteNotifications(UIApplication application, NSError error) =>
        IosPushNotifications.OnFailed(error);
}
