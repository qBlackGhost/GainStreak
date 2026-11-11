namespace GymApp.Server.Data;

/// <summary>The user's invite code for adding friends by QR code or link (Phase 21). One per user; resetting replaces
/// the code. Deleted with the user.</summary>
public class FriendInvite
{
    public int UserId { get; set; }
    public string Code { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
