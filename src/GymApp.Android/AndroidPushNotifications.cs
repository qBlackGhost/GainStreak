using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Gms.Tasks;
using Firebase;
using Firebase.Messaging;
using GymApp.App;
using GymApp.Contracts;
using GymApp.Services;
using GmsTask = Android.Gms.Tasks.Task;
using Task = System.Threading.Tasks.Task;

namespace GymApp.Android;

/// <summary>
/// Friend notifications through Firebase Cloud Messaging (Phase 22). Supported only when the build had a
/// google-services.json (see the project file); without one Firebase never starts and the app hides the feature. The
/// permission is the reminders' POST_NOTIFICATIONS (16g). Firebase shows a push itself while the app is in the
/// background; in the foreground <see cref="PushMessagingService"/> gets it and shows it unless Inbox or Friends is open.
/// </summary>
public sealed class AndroidPushNotifications : IPushNotifications
{
    public const string ChannelId = "friends";
    private const string Prefs = "push", AskedKey = "asked";

    private static event Action? Changed;
    private readonly Context _context;

    public AndroidPushNotifications(Context context)
    {
        _context = context;
        EnsureChannel(context);
    }

    public bool IsSupported => IsFirebaseReady(_context);

    public event Action? TokenChanged
    {
        add => Changed += value;
        remove => Changed -= value;
    }

    /// <summary>From <see cref="PushMessagingService.OnNewToken"/>: FCM made or replaced this install's token.</summary>
    internal static void OnNewToken() => Changed?.Invoke();

    public static bool IsFirebaseReady(Context context)
    {
        try { return FirebaseApp.GetApps(context).Count > 0; }
        catch (Exception) { return false; }
    }

    public Task<PushPermission> GetPermissionAsync()
    {
        var manager = (NotificationManager)_context.GetSystemService(Context.NotificationService)!;
        if (manager.AreNotificationsEnabled()) return Task.FromResult(PushPermission.Allowed);
        // Before Android 13 there's no prompt, only the app's notification setting
        if (!OperatingSystem.IsAndroidVersionAtLeast(33)) return Task.FromResult(PushPermission.Denied);
        var asked = _context.GetSharedPreferences(Prefs, FileCreationMode.Private)!.GetBoolean(AskedKey, false);
        var granted = _context.CheckSelfPermission(Manifest.Permission.PostNotifications) == Permission.Granted;
        return Task.FromResult(granted ? PushPermission.Allowed : asked ? PushPermission.Denied : PushPermission.NotAsked);
    }

    public async Task<bool> RequestPermissionAsync()
    {
        var granted = await MainActivity.RequestNotificationPermissionAsync(_context);
        _context.GetSharedPreferences(Prefs, FileCreationMode.Private)!.Edit()!.PutBoolean(AskedKey, true)!.Apply();
        return granted;
    }

    public async Task<PushToken?> GetTokenAsync()
    {
        if (!IsSupported) return null;
        try
        {
            // Marked obsolete by the binding only; getToken() is Firebase's current call
#pragma warning disable CS0618
            var token = await Await(FirebaseMessaging.Instance.GetToken());
#pragma warning restore CS0618
            return token?.ToString() is { Length: > 0 } t ? new PushToken(PushPlatform.Fcm, t, false) : null;
        }
        catch (Exception)
        {
            return null;   // no Play services (some emulators), or no network yet: the next resume tries again
        }
    }

    /// <summary>The "Friends" channel, next to Reminders and Workout in progress. High importance, so a request pops up.</summary>
    public static void EnsureChannel(Context context)
    {
        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        if (manager.GetNotificationChannel(ChannelId) is null)
            manager.CreateNotificationChannel(new NotificationChannel(ChannelId, "Friends", NotificationImportance.High)
            {
                Description = "Friend requests, and when someone accepts yours or adds you with your QR code"
            });
    }

    /// <summary>A push that came while the app is open, shown like Firebase shows one in the background: same channel,
    /// icon and tag, and a tap opens the app with the push's <c>kind</c>.</summary>
    public static void Show(Context context, string title, string body, IDictionary<string, string> data)
    {
        var kind = data.TryGetValue(PushPayload.Kind, out var k) ? k : "";
        var tag = kind + "-" + (data.TryGetValue(PushPayload.Sender, out var f) ? f : "");
        var open = new Intent(context, typeof(MainActivity))
            .PutExtra(PushPayload.Kind, kind)
            .AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        var tap = PendingIntent.GetActivity(context, tag.GetHashCode(), open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var notification = new Notification.Builder(context, ChannelId)
            .SetSmallIcon(Resource.Drawable.ic_notification)
            .SetContentTitle(title)
            .SetContentText(body)
            .SetStyle(new Notification.BigTextStyle().BigText(body))
            .SetContentIntent(tap)
            .SetAutoCancel(true)
            .Build();
        ((NotificationManager)context.GetSystemService(Context.NotificationService)!).Notify(tag, 0, notification);
    }

    private static Task<Java.Lang.Object?> Await(GmsTask task)
    {
        var done = new TaskCompletionSource<Java.Lang.Object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        task.AddOnCompleteListener(new Listener(done));
        return done.Task;
    }

    private sealed class Listener(TaskCompletionSource<Java.Lang.Object?> done) : Java.Lang.Object, IOnCompleteListener
    {
        public void OnComplete(GmsTask task)
        {
            if (task.IsSuccessful) done.TrySetResult(task.Result);
            else done.TrySetException(task.Exception ?? new Exception("FCM gave no token."));
        }
    }
}

/// <summary>Firebase's callbacks: a new token, and a push that came while the app is in the foreground (in the
/// background Android shows it without the app).</summary>
[Service(Exported = false)]
[IntentFilter(["com.google.firebase.MESSAGING_EVENT"])]
public sealed class PushMessagingService : FirebaseMessagingService
{
    // The binding marks it obsolete, but it's still how Firebase reports a new token (FirebaseMessagingService.onNewToken)
#pragma warning disable CS0672
    public override void OnNewToken(string token) => AndroidPushNotifications.OnNewToken();
#pragma warning restore CS0672

    public override async void OnMessageReceived(RemoteMessage message)
    {
        try
        {
            var data = message.Data;
            data.TryGetValue(PushPayload.Kind, out var kind);
            if (!await GymApplication.PushArrivedAsync(kind)) return;
            var shown = message.GetNotification();
            AndroidPushNotifications.Show(this, shown?.Title ?? "GainStreak", shown?.Body ?? "", data);
        }
        catch (Exception e)
        {
            global::Android.Util.Log.Warn("GainStreak", $"Push: couldn't show a push: {e.Message}");
        }
    }
}
