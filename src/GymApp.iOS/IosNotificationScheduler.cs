using Foundation;
using GymApp.App;
using GymApp.Services;
using UserNotifications;

namespace GymApp.iOS;

/// <summary>The reminders (16g) as local notifications at a calendar date and time, so they keep their clock time
/// across a daylight saving change. A new plan removes the app's pending requests first, except the workout nudge
/// (Phase 18), which has its own identifier.</summary>
public sealed class IosNotificationScheduler : INotificationScheduler
{
    private const string NudgeId = "workout-nudge";

    public bool IsSupported => true;

    public async Task<bool> RequestPermissionAsync()
    {
        var (granted, _) = await UNUserNotificationCenter.Current.RequestAuthorizationAsync(
            UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound);
        return granted;
    }

    public void Replace(IReadOnlyList<PlannedReminder> reminders)
    {
        var center = UNUserNotificationCenter.Current;
        center.GetPendingNotificationRequests(pending =>
        {
            var old = pending.Select(p => p.Identifier).Where(id => id != NudgeId).ToArray();
            if (old.Length > 0) center.RemovePendingNotificationRequests(old);
            Add(center, reminders);
        });
    }

    private static void Add(UNUserNotificationCenter center, IReadOnlyList<PlannedReminder> reminders)
    {
        foreach (var r in reminders)
        {
            if (r.LocalTime <= DateTime.Now) continue;
            var content = new UNMutableNotificationContent { Title = r.Title, Body = r.Body, Sound = UNNotificationSound.Default };
            var when = new NSDateComponents
            {
                Year = r.Date.Year, Month = r.Date.Month, Day = r.Date.Day, Hour = r.Time.Hour, Minute = r.Time.Minute
            };
            var request = UNNotificationRequest.FromIdentifier(r.Id.ToString(), content,
                UNCalendarNotificationTrigger.CreateTrigger(when, repeats: false));
            center.AddNotificationRequest(request, _ => { });
        }
    }

    public void ScheduleWorkoutNudge(DateTimeOffset at, string title, string body)
    {
        var seconds = Math.Max(1, (at - DateTimeOffset.Now).TotalSeconds);
        var content = new UNMutableNotificationContent { Title = title, Body = body, Sound = UNNotificationSound.Default };
        var request = UNNotificationRequest.FromIdentifier(NudgeId, content,
            UNTimeIntervalNotificationTrigger.CreateTrigger(seconds, repeats: false));
        UNUserNotificationCenter.Current.AddNotificationRequest(request, _ => { });   // the same id replaces an earlier nudge
    }

    public void CancelWorkoutNudge()
    {
        var center = UNUserNotificationCenter.Current;
        center.RemovePendingNotificationRequests([NudgeId]);
        center.RemoveDeliveredNotifications([NudgeId]);
    }
}

/// <summary>Shows a reminder that comes while the app is open, and opens Home when one is tapped.</summary>
public sealed class ReminderDelegate : UNUserNotificationCenterDelegate
{
    public override void WillPresentNotification(UNUserNotificationCenter center, UNNotification notification,
        Action<UNNotificationPresentationOptions> completionHandler) =>
        completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.Sound);

    public override void DidReceiveNotificationResponse(UNUserNotificationCenter center, UNNotificationResponse response,
        Action completionHandler)
    {
        GymApplication.OpenFromReminder();
        completionHandler();
    }
}
