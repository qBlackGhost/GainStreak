using Avalonia;
using Avalonia.iOS;
using Foundation;
using UserNotifications;
using GymApp.App;

namespace GymApp.iOS;

/// <summary>Starts the shared app (<see cref="GymApplication"/>) with the iOS platform.</summary>
[Register("AppDelegate")]
public partial class AppDelegate : AvaloniaAppDelegate<GymApplication>
{
    private readonly ReminderDelegate _reminders = new();

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        PhonePlatform.UseDebugServer(BuiltSettings.DebugServerUrl);
        GymApplication.Platform = new IosPlatform();
        // Before launching ends, so a tap that started the app is delivered too (16g)
        UNUserNotificationCenter.Current.Delegate = _reminders;
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }
}
