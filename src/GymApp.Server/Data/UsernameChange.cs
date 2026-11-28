namespace GymApp.Server.Data;

/// <summary>One rename (Phase 28), kept so a report against a name that changed afterwards can still be understood.
/// Never sent to clients. Deleted with the user.</summary>
public class UsernameChange
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public string OldUsername { get; set; } = "";
    public string NewUsername { get; set; } = "";
    public DateTime ChangedAt { get; set; }
}
