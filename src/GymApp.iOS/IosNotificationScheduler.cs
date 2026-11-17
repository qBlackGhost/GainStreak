using Foundation;
using GymApp.App;
using GymApp.Services;
using GymApp.Contracts;
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

/// <summary>
/// Every notification the app gets: the reminders and the workout nudge (local, 16g and Phase 18), and friend
/// notifications (pushes, Phase 22, which carry a <c>kind</c>). A reminder that comes while the app is open shows, and a
/// tap opens Home. A push while the app is open shows unless Inbox or Friends is on screen (they reload instead); a tap
/// opens the Inbox's requests or Friends.
/// </summary>
public sealed class NotificationDelegate : UNUserNotificationCenterDelegate
{
    private const UNNotificationPresentationOptions Show =
        UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.List | UNNotificationPresentationOptions.Sound;

    private static string? PushKind(UNNotification notification) =>
        notification.Request.Content.UserInfo.TryGetValue(new NSString(PushPayload.Kind), out var kind) ? kind?.ToString() : null;

    public override void WillPresentNotification(UNUserNotificationCenter center, UNNotification notification,
        Action<UNNotificationPresentationOptions> completionHandler)
    {
        if (PushKind(notification) is not { } kind)
        {
            completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.Sound);
            return;
        }
        _ = GymApplication.PushArrivedAsync(kind).ContinueWith(t =>
            completionHandler(!t.IsCompletedSuccessfully || t.Result ? Show : UNNotificationPresentationOptions.None));
    }

    public override void DidReceiveNotificationResponse(UNUserNotificationCenter center, UNNotificationResponse response,
        Action completionHandler)
    {
        if (PushKind(response.Notification) is { } kind) GymApplication.OpenFromPush(kind);
        else GymApplication.OpenFromReminder();
        completionHandler();
    }
}
