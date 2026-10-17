using System;
using GymApp.Core;

namespace GymApp.Data.Models;

/// <summary>One row per pair of users, whatever its state. The pair is always stored with UserAId &lt; UserBId
/// (enforced by the service), so the unique index on (UserAId, UserBId) allows it only once. Declining, cancelling
/// and removing delete the row; there is no stored "declined" state, so a new request can be sent later.</summary>
public class Friendship
{
    public int Id { get; set; }
    public int UserAId { get; set; }
    public User UserA { get; set; } = null!;
    public int UserBId { get; set; }
    public User UserB { get; set; } = null!;
    public int RequestedById { get; set; }            // UserAId or UserBId: who sent the request
    public FriendshipStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AcceptedAt { get; set; }
}
