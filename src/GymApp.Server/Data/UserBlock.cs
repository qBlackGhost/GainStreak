namespace GymApp.Server.Data;

/// <summary><see cref="BlockerId"/> blocked <see cref="BlockedId"/>. Each direction is its own row, so both can block
/// each other and each lifts only their own. Deleted with either user.</summary>
public class UserBlock
{
    public int BlockerId { get; set; }
    public int BlockedId { get; set; }
    public DateTime CreatedAt { get; set; }
}
