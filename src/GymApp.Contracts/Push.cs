namespace GymApp.Contracts;

/// <summary>Which push service delivers to a device: Apple's (iPhone) or Firebase Cloud Messaging (Android). Sent as
/// <c>apns</c> or <c>fcm</c>. Phase 22.</summary>
public enum PushPlatform { Apns, Fcm }

/// <summary>This session's push token. <see cref="Sandbox"/> is true for an iPhone build that talks to APNs' sandbox
/// (Debug builds, the Simulator); always false for FCM. Phase 22.</summary>
public sealed record PushDeviceRequest(PushPlatform Platform, string Token, bool Sandbox = false);

/// <summary>What a push is about, in its data as <see cref="PushPayload.Kind"/> (camelCase), so the phone knows what a
/// tap opens. Phase 22.</summary>
public enum PushKind { FriendRequest, FriendAccepted, InviteAccepted }

/// <summary>The data keys a push carries besides its text, shared by the server and the phones.</summary>
public static class PushPayload
{
    /// <summary>The <see cref="PushKind"/>, camelCase: <c>friendRequest</c>, <c>friendAccepted</c>, <c>inviteAccepted</c>.</summary>
    public const string Kind = "kind";

    /// <summary>The user id of whoever caused it, as a string. Not "from": FCM refuses that key in a data payload
    /// (found against the real FCM in 22e).</summary>
    public const string Sender = "sender";

    /// <summary>Keys FCM refuses or uses itself in a data payload (besides any starting "google" or "gcm"), so the
    /// payload's keys must avoid them.</summary>
    public static readonly IReadOnlyList<string> FcmReservedKeys = ["from", "notification", "message_type", "collapse_key"];

    /// <summary>The camelCase name the server writes for <paramref name="kind"/>, the same as the JSON enum format.</summary>
    public static string Name(PushKind kind) => kind switch
    {
        PushKind.FriendRequest => "friendRequest",
        PushKind.FriendAccepted => "friendAccepted",
        PushKind.InviteAccepted => "inviteAccepted",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>The kind for a name from <see cref="Name"/>; null for anything else (a newer server's kind, say).</summary>
    public static PushKind? Parse(string? name) =>
        Enum.GetValues<PushKind>().Cast<PushKind?>().FirstOrDefault(k => Name(k!.Value) == name);
}

/// <summary>Limits on a push token, checked by the server.</summary>
public static class PushRules
{
    /// <summary>APNs tokens are 64 hex characters and FCM's about 160; this leaves room and keeps the unique index small.</summary>
    public const int MaxTokenLength = 512;

    public const string BadTokenMessage = "That push token isn't valid.";

    /// <summary>Not empty, not too long, printable ASCII without spaces (both services' tokens are).</summary>
    public static bool IsValidToken(string? token) =>
        !string.IsNullOrEmpty(token) && token.Length <= MaxTokenLength && token.All(c => c is > ' ' and < (char)127);
}
