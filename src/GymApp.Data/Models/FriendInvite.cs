using System;

namespace GymApp.Data.Models;

/// <summary>An account's invite code for adding friends by QR code or link (Phase 21), for the TestBench's local mode;
/// on the server it's the server's own table. One per user; deleted with the user.</summary>
public class FriendInvite
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string Code { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
