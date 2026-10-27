using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using GymApp.App;

namespace GymApp.Android;

/// <summary>Starts the shared app (<see cref="GymApplication"/>) with the Android platform.</summary>
[Application]
public class AndroidApp(nint javaReference, JniHandleOwnership transfer)
    : AvaloniaAndroidApplication<GymApplication>(javaReference, transfer)
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        PhonePlatform.UseDebugServer(BuiltSettings.DebugServerUrl);
        GymApplication.Platform = new AndroidPlatform(this);
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }
}
