namespace GymApp.Server.Data;

/// <summary>A badge a user earned (Phase 25), as their phone uploaded it. The phone works badges out; the server only
/// keeps them for friends to see. One row per user and badge.</summary>
public class UserBadge
{
    public const int MaxIdLength = 40;

    public int UserId { get; set; }
    public string BadgeId { get; set; } = "";
    public int Tier { get; set; }
    public DateOnly EarnedOn { get; set; }
}

/// <summary>Someone became <see cref="InviterId"/>'s friend through their invite (Phase 25, the Recruiter badge). One row
/// per pair; kept when the friendship ends, since the badge is kept too.</summary>
public class InviteAccept
{
    public int InviterId { get; set; }
    public int InviteeId { get; set; }
    public DateTime AcceptedAt { get; set; }
}
