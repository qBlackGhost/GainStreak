using Foundation;
using GymApp.App;
using GymApp.Services;
using UserNotifications;

namespace GymApp.iOS;

/// <summary>The reminders (16g) as local notifications at a calendar date and time, so they keep their clock time
/// across a daylight saving change. The app owns all its pending requests, so a new plan removes them all first.</summary>
public sealed class IosNotificationScheduler : INotificationScheduler
{
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
        center.RemoveAllPendingNotificationRequests();
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
