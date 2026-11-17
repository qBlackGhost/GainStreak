using System.Text;
using GymApp.Contracts;
using GymApp.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Server.Push;

/// <summary>
/// <c>dotnet GymApp.Server.dll push-test &lt;username&gt;</c>: sends a test notification to that user's phones and says how
/// each went, so a deploy can check the APNs and FCM keys. A command like <c>suspend</c>, not an endpoint: running it
/// takes the server's settings. Prints the platform and result per device, never a token.
/// </summary>
public static class PushCommands
{
    public const string TestBody = "Test notification from GainStreak.";

    public static bool IsCommand(string[] args) => args is ["push-test", _];

    public static async Task<(bool Ok, string Message)> RunAsync(string[] args, ServerDbContext db, FriendNotifier notifier)
    {
        var normalized = User.NormalizeUsername(args[1]);
        var user = await db.Users.FirstOrDefaultAsync(u => u.UsernameNormalized == normalized);
        if (user is null) return (false, $"No user is called {args[1]}.");
        if (!notifier.IsConfigured) return (false, "No push service is configured: set Push__Apns__* or Push__Fcm__ServiceAccountJson.");

        var devices = await db.PushDevices.CountAsync(d => d.UserId == user.Id);
        if (devices == 0) return (false, $"{user.Username} (user {user.Id}) has no phone registered for notifications.");

        // A friend request from themselves with the test text: the phone handles it like any other, a tap opens the Inbox
        var deliveries = await notifier.SendToUserAsync(user.Id, new PushMessage(PushKind.FriendRequest, user.Id, user.Username, TestBody));
        var text = new StringBuilder($"{user.Username} (user {user.Id}): {deliveries.Count} of {devices} device(s) tried.");
        foreach (var d in deliveries)
            text.Append($"\n  device {d.DeviceId}: {d.Platform}{(d.Sandbox ? " (sandbox)" : "")}, {Describe(d.Result)}");
        if (deliveries.Count < devices)
            text.Append("\n  The others are on expired sessions or a platform with no keys set.");
        return (deliveries.Any(d => d.Result == PushSendResult.Sent), text.ToString());
    }

    private static string Describe(PushSendResult r) => r switch
    {
        PushSendResult.Sent => "sent",
        PushSendResult.DeadToken => "token no longer valid (deleted)",
        _ => "failed (see the log above)"
    };
}
