using GymApp.Contracts;

namespace GymApp.Server.Data;

/// <summary>A problem report or an idea sent from the app's Feedback popup. The table is the record; the Discord
/// message (<see cref="Feedback.DiscordFeedbackForwarder"/>) is only the notification. Deleted with the account.</summary>
public class FeedbackEntry
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public FeedbackKind Kind { get; set; }
    public string Text { get; set; } = "";
    public string? Report { get; set; }
    /// <summary>The X-App-Version header, e.g. "0.15.0"; null if the app sent none.</summary>
    public string? AppVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>When the Discord message went out; null if forwarding is off or failed.</summary>
    public DateTime? ForwardedAt { get; set; }
}
