using Android.App;
using Android.Content;
using GymApp.App;
using GymApp.Services;

namespace GymApp.Android;

/// <summary>The reminders (16g) as alarms: each fires <see cref="ReminderReceiver"/>, which posts the notification.
/// Inexact (<c>SetAndAllowWhileIdle</c>), so no exact-alarm permission: Android may deliver a few minutes late. The ids
/// scheduled last are kept, so the next plan can cancel them first. Alarms don't survive a reboot; the next start of
/// the app plans them again.</summary>
public sealed class AndroidNotificationScheduler(Context context) : INotificationScheduler
{
    public const string ChannelId = "reminders";
    private const string Prefs = "gymapp_reminders", IdsKey = "ids";

    public bool IsSupported => true;

    public Task<bool> RequestPermissionAsync() => MainActivity.RequestNotificationPermissionAsync(context);

    public void Replace(IReadOnlyList<PlannedReminder> reminders)
    {
        var alarms = (AlarmManager)context.GetSystemService(Context.AlarmService)!;
        var prefs = context.GetSharedPreferences(Prefs, FileCreationMode.Private)!;
        foreach (var id in prefs.GetStringSet(IdsKey, null) ?? [])
            alarms.Cancel(Pending(int.Parse(id), null));

        var now = DateTimeOffset.Now;
        var ids = new List<string>();
        foreach (var r in reminders)
        {
            var at = r.At(TimeZoneInfo.Local);
            if (at <= now) continue;
            alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, at.ToUnixTimeMilliseconds(), Pending(r.Id, r));
            ids.Add(r.Id.ToString());
        }
        prefs.Edit()!.PutStringSet(IdsKey, ids)!.Apply();
    }

    /// <summary>The alarm's intent; the same id gives the same PendingIntent, which is how it's cancelled.</summary>
    private PendingIntent Pending(int id, PlannedReminder? r)
    {
        var intent = new Intent(context, typeof(ReminderReceiver)).SetAction(ReminderReceiver.Action);
        if (r is not null)
            intent.PutExtra(ReminderReceiver.IdExtra, id).PutExtra(ReminderReceiver.TitleExtra, r.Title)
                  .PutExtra(ReminderReceiver.BodyExtra, r.Body);
        return PendingIntent.GetBroadcast(context, id, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
    }

    /// <summary>The "Reminders" channel, which the user can turn off in the system settings.</summary>
    public static void EnsureChannel(Context context)
    {
        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        if (manager.GetNotificationChannel(ChannelId) is not null) return;
        manager.CreateNotificationChannel(new NotificationChannel(ChannelId, "Reminders", NotificationImportance.Default)
        {
            Description = "Training-day and missed-day reminders"
        });
    }
}

/// <summary>An alarm went off: post its reminder. Tapping it opens the app on Home.</summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class ReminderReceiver : BroadcastReceiver
{
    public const string Action = "com.gainstreak.app.REMINDER";
    public const string IdExtra = "id", TitleExtra = "title", BodyExtra = "body";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null) return;
        var id = intent.GetIntExtra(IdExtra, 0);
        var title = intent.GetStringExtra(TitleExtra) ?? "GainStreak";
        var body = intent.GetStringExtra(BodyExtra) ?? "";
        AndroidNotificationScheduler.EnsureChannel(context);

        var open = new Intent(context, typeof(MainActivity))
            .PutExtra(MainActivity.FromReminderExtra, true)
            .AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        var tap = PendingIntent.GetActivity(context, id, open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        var notification = new Notification.Builder(context, AndroidNotificationScheduler.ChannelId)
            .SetSmallIcon(Resource.Drawable.ic_notification)
            .SetContentTitle(title)
            .SetContentText(body)
            .SetStyle(new Notification.BigTextStyle().BigText(body))
            .SetContentIntent(tap)
            .SetAutoCancel(true)
            .Build();
        ((NotificationManager)context.GetSystemService(Context.NotificationService)!).Notify(id, notification);
    }
}
