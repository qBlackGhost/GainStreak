using GymApp.Core;

namespace GymApp.Server.Data;

/// <summary>Same shape and rules as the app's Friendship: one row per pair, <see cref="UserAId"/> &lt; <see cref="UserBId"/>,
/// deleted with either user.</summary>
public class Friendship
{
    public long Id { get; set; }
    public int UserAId { get; set; }
    public int UserBId { get; set; }

    /// <summary>One of the pair: who sent the request. Only the other one can accept it.</summary>
    public int RequestedById { get; set; }

    public FriendshipStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
}
