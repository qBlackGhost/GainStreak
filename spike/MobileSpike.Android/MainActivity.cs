using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace MobileSpike.Android;

[Activity(Label = "GainStreak spike", Theme = "@style/SpikeTheme", Icon = "@drawable/icon", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity;
