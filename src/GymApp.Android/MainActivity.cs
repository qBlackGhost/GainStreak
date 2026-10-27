using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace GymApp.Android;

[Activity(Label = "GainStreak", Theme = "@style/GainStreakTheme", Icon = "@mipmap/ic_launcher", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity;
