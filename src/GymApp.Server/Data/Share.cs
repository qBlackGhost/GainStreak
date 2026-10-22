namespace GymApp.Server.Data;

/// <summary>A split or a workout one friend sent another, waiting until the receiver adds or dismisses it, or until
/// <see cref="Contracts.ShareRules.KeptFor"/> passes. <see cref="Payload"/> is the shared split or workout as JSON,
/// a snapshot: changes the sender makes later don't reach it. Deleted with either user.</summary>
public class Share
{
    public const int MaxPayloadLength = 64_000;

    public long Id { get; set; }
    public int FromUserId { get; set; }
    public int ToUserId { get; set; }
    public ShareKind Kind { get; set; }
    public string Payload { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public enum ShareKind { Split, Workout }
