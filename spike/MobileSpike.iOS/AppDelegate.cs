using Avalonia;
using Avalonia.iOS;
using Foundation;

namespace MobileSpike.iOS;

[Register("AppDelegate")]
public partial class AppDelegate : AvaloniaAppDelegate<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        SpikeLog.Mark("iOS app delegate");
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }
}
