using System.Globalization;
using GymApp.Contracts;

namespace GymApp.Server.Push;

/// <summary>One friend notification: what happened and who did it. The text comes from here, so both senders and the
/// tests agree. Only the username leaves the server, through Apple or Google; never the email or any stats.
/// The title says what happened (approved on the design canvas, 22d): Android already shows the app's name in the
/// notification's header. <see cref="Text"/> replaces the usual sentence, and the title is then the app's name (the
/// <c>push-test</c> command).</summary>
public sealed record PushMessage(PushKind Kind, int SenderId, string SenderUsername, string? Text = null)
{
    public const string AppName = "GainStreak";

    public string Title => Text is not null ? AppName : Kind switch
    {
        PushKind.FriendRequest => "Friend request",
        PushKind.FriendAccepted => "Request accepted",
        PushKind.InviteAccepted => "New friend",
        _ => throw new ArgumentOutOfRangeException(nameof(Kind))
    };

    public string Body => Text ?? Kind switch
    {
        PushKind.FriendRequest => $"{SenderUsername} wants to be your friend.",
        PushKind.FriendAccepted => $"{SenderUsername} accepted your friend request.",
        PushKind.InviteAccepted => $"{SenderUsername} added you with your QR code. You're now friends.",
        _ => throw new ArgumentOutOfRangeException(nameof(Kind))
    };

    /// <summary>One notification per sender and kind on the phone: a newer one replaces it (APNs collapse id, Android tag).</summary>
    public string CollapseKey => $"{PushPayload.Name(Kind)}-{SenderId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The data besides the text, for the tap.</summary>
    public IReadOnlyDictionary<string, string> Data => new Dictionary<string, string>
    {
        [PushPayload.Kind] = PushPayload.Name(Kind),
        [PushPayload.Sender] = SenderId.ToString(CultureInfo.InvariantCulture)
    };
}

/// <summary>Where a push goes: a token on one push service.</summary>
public sealed record PushTarget(PushPlatform Platform, string Token, bool Sandbox);

public enum PushSendResult
{
    Sent,
    /// <summary>The service says the token is gone (app removed, token replaced): delete it.</summary>
    DeadToken,
    /// <summary>Anything else; logged by the sender, the token is kept.</summary>
    Failed
}

/// <summary>Delivers to one push service (APNs or FCM, 22b). Unconfigured services have no sender, and their tokens are
/// kept but skipped. Should honour the token for the time limit; <see cref="FriendNotifier"/> stops waiting anyway.</summary>
public interface IPushSender
{
    PushPlatform Platform { get; }

    Task<PushSendResult> SendAsync(PushTarget target, PushMessage message, CancellationToken ct);
}
