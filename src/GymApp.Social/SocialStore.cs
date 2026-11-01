using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GymApp.Core;

namespace GymApp.Social;

/// <summary>A user as friends see them. Only public fields, never body data, logs or gyms.</summary>
public record SocialUser(int Id, string Username);

/// <summary>A friendship row. <see cref="UserAId"/> &lt; <see cref="UserBId"/> always; the service builds it that way.</summary>
public record FriendshipRecord(int UserAId, int UserBId, int RequestedById, FriendshipStatus Status, DateTime CreatedAt, DateTime? AcceptedAt)
{
    public int Other(int userId) => userId == UserAId ? UserBId : UserAId;
    public bool IsIncomingFor(int userId) => Status == FriendshipStatus.Pending && RequestedById != userId;
    public bool IsOutgoingFor(int userId) => Status == FriendshipStatus.Pending && RequestedById == userId;
}

/// <summary>A friendship seen from one user, with the other user's public details.</summary>
public record FriendLink(FriendshipRecord Friendship, SocialUser Other);

/// <summary>One user blocking another, seen from one of the two: <see cref="Other"/> is the other one.</summary>
public record BlockRecord(int BlockerId, int BlockedId, DateTime CreatedAt, SocialUser Other);

/// <summary>The stats friends may see: username, points and current streak. Never body data, logs, PRs or gyms.</summary>
public record PublicStats(int UserId, string Username, int TotalPoints, int WeekPoints, int CurrentStreak, string? Avatar = null);

/// <summary>The write was refused because another write got there first (e.g. the pair already exists).</summary>
public class SocialStoreConflictException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Where friends and public stats live. The local store reads this install's database; a server-backed store can
/// replace it later without changes to the services or UI, so nothing here exposes EF types.
/// </summary>
/// <remarks>Also the friends map's check-in source (<see cref="IFriendCheckInSource"/>), for the local mode and the server.</remarks>
public interface ISocialStore : IFriendCheckInSource
{
    /// <summary>A unit of work. Writes are only kept after <see cref="ISocialSession.CommitAsync"/>, and reads and
    /// writes in one session see a consistent state, so a check-then-write can't race another click.</summary>
    Task<ISocialSession> OpenAsync();

    /// <summary>Brings the users' cached points and streaks up to date before they're read. A streak can break at
    /// midnight without anything being logged, so a friend who hasn't opened the app would otherwise show a stale one.
    /// Call it outside a session.</summary>
    Task RefreshStatsAsync(IReadOnlyCollection<int> userIds, DateOnly today);
}

public interface ISocialSession : IAsyncDisposable
{
    Task<SocialUser?> FindUserAsync(int userId);

    /// <summary>Users whose username starts with <paramref name="prefix"/> (ignoring case), by username.</summary>
    Task<IReadOnlyList<SocialUser>> SearchUsersAsync(string prefix, IReadOnlyCollection<int> excludeUserIds, int limit);

    /// <summary>Every friendship row involving the user, pending or accepted.</summary>
    Task<IReadOnlyList<FriendLink>> GetFriendshipsAsync(int userId);

    /// <summary>The row for the pair, with <paramref name="userAId"/> &lt; <paramref name="userBId"/>.</summary>
    Task<FriendshipRecord?> GetFriendshipAsync(int userAId, int userBId);

    Task AddFriendshipAsync(FriendshipRecord friendship);
    Task UpdateFriendshipAsync(FriendshipRecord friendship);
    Task DeleteFriendshipAsync(int userAId, int userBId);

    /// <summary>Every block involving the user, in either direction.</summary>
    Task<IReadOnlyList<BlockRecord>> GetBlocksAsync(int userId);

    Task AddBlockAsync(int blockerId, int blockedId, DateTime at);
    Task DeleteBlockAsync(int blockerId, int blockedId);

    /// <summary>Public stats for each user that exists. <see cref="PublicStats.WeekPoints"/> sums the points dated
    /// <paramref name="weekStart"/> to <paramref name="weekEnd"/>, both included. <paramref name="today"/> is the
    /// viewer's date: a store that keeps projected stats picks each user's entry for it; one that refreshed can ignore it.</summary>
    Task<IReadOnlyList<PublicStats>> GetPublicStatsAsync(IReadOnlyCollection<int> userIds, DateOnly weekStart, DateOnly weekEnd, DateOnly today);

    /// <summary>Keeps the writes. Throws <see cref="SocialStoreConflictException"/> if another write got there first.</summary>
    Task CommitAsync();
}

/// <summary>Where the friends map gets friends' check-ins: the local store, or the app's remote source (the server).</summary>
public interface IFriendCheckInSource
{
    /// <summary>
    /// Gym check-ins of the user's accepted friends from <paramref name="since"/> on (inclusive): only friends who share
    /// them (<c>ShareGymCheckIns == true</c>; not asked yet means no) and only logs with a gym. Gym and day only, never
    /// a time; a custom gym never comes with coordinates. A read, so it needs no session.
    /// </summary>
    Task<IReadOnlyList<FriendCheckIn>> GetFriendGymCheckInsAsync(int userId, DateOnly since);
}
