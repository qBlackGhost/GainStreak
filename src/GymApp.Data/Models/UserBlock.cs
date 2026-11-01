using System;

namespace GymApp.Data.Models;

/// <summary>A block between two accounts on this install (the TestBench's local mode; on the server it's the server's
/// own table). Each direction is its own row.</summary>
public class UserBlock
{
    public int BlockerId { get; set; }
    public User Blocker { get; set; } = null!;
    public int BlockedId { get; set; }
    public User Blocked { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
