using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace MobileSpike.Android;

[Application]
public class AndroidApp(nint javaReference, JniHandleOwnership transfer) : AvaloniaAndroidApplication<App>(javaReference, transfer)
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        SpikeLog.Mark("Android application");
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }
}
