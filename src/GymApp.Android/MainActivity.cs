using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;
using GymApp.App;

namespace GymApp.Android;

[Activity(Label = "GainStreak", Theme = "@style/GainStreakTheme", Icon = "@mipmap/ic_launcher", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode,
    LaunchMode = LaunchMode.SingleTask)]
// Invite links (Phase 21): https://gainstreak.app/add/<code> opens the app, verified against the server's
// /.well-known/assetlinks.json. Avalonia passes the link on as an OpenUri activation (GymApplication)
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "https", DataHost = "gainstreak.app", DataPathPrefix = "/add/", AutoVerify = true)]
public class MainActivity : AvaloniaMainActivity
{
    /// <summary>On the intent a tapped reminder opens (16g): go to Home.</summary>
    public const string FromReminderExtra = "fromReminder";

    private const int NotificationPermissionRequest = 1601;
    private const int LocationPermissionRequest = 1801;
    private static WeakReference<MainActivity>? _current;
    private static TaskCompletionSource<bool>? _permission;
    private static TaskCompletionSource<bool>? _location;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _current = new WeakReference<MainActivity>(this);
        if (Intent?.GetBooleanExtra(FromReminderExtra, false) == true) GymApplication.OpenFromReminder();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        if (intent?.GetBooleanExtra(FromReminderExtra, false) == true) GymApplication.OpenFromReminder();
    }

    /// <summary>Android 13 and later ask for POST_NOTIFICATIONS when the user turns Reminders on; earlier versions
    /// only have the app's notification setting.</summary>
    public static Task<bool> RequestNotificationPermissionAsync(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            return Task.FromResult(((NotificationManager)context.GetSystemService(NotificationService)!).AreNotificationsEnabled());
        if (context.CheckSelfPermission(Manifest.Permission.PostNotifications) == Permission.Granted) return Task.FromResult(true);
        if (_current?.TryGetTarget(out var activity) != true) return Task.FromResult(false);

        _permission?.TrySetResult(false);
        _permission = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        activity!.RequestPermissions([Manifest.Permission.PostNotifications], NotificationPermissionRequest);
        return _permission.Task;
    }

    /// <summary>"Verified at gym" (18c): fine and coarse location together, as Android 12 and later require (the user may
    /// pick approximate). True if either is allowed.</summary>
    public static Task<bool> RequestLocationPermissionAsync(Context context)
    {
        if (AndroidLocationProvider.Allowed(context)) return Task.FromResult(true);
        if (_current?.TryGetTarget(out var activity) != true) return Task.FromResult(false);

        _location?.TrySetResult(false);
        _location = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        activity!.RequestPermissions([Manifest.Permission.AccessFineLocation, Manifest.Permission.AccessCoarseLocation],
            LocationPermissionRequest);
        return _location.Task;
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == NotificationPermissionRequest)
            _permission?.TrySetResult(grantResults.Length > 0 && grantResults[0] == Permission.Granted);
        if (requestCode == LocationPermissionRequest)
            _location?.TrySetResult(grantResults.Any(r => r == Permission.Granted));
    }
}
