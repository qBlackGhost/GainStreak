using GymApp.Contracts;

namespace GymApp.Server.Data;

/// <summary>A user reported from the app. Read in the Discord channel, like feedback; deleted with either user.</summary>
public class UserReport
{
    public int Id { get; set; }
    public int ReporterId { get; set; }
    public int ReportedUserId { get; set; }
    public UserReportReason Reason { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>When it reached Discord; null if it hasn't (no webhook, or the post failed).</summary>
    public DateTime? ForwardedAt { get; set; }
}
