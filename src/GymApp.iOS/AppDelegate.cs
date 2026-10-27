using Avalonia;
using Avalonia.iOS;
using Foundation;
using GymApp.App;

namespace GymApp.iOS;

/// <summary>Starts the shared app (<see cref="GymApplication"/>) with the iOS platform.</summary>
[Register("AppDelegate")]
public partial class AppDelegate : AvaloniaAppDelegate<GymApplication>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        PhonePlatform.UseDebugServer(BuiltSettings.DebugServerUrl);
        GymApplication.Platform = new IosPlatform();
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }
}
