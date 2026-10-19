using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GymApp.Services;

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

/// <summary>The stats friends may see: username, points and current streak. Never body data, logs, PRs or gyms.</summary>
public record PublicStats(int UserId, string Username, int TotalPoints, int WeekPoints, int CurrentStreak);

/// <summary>The write was refused because another write got there first (e.g. the pair already exists).</summary>
public class SocialStoreConflictException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Where friends and public stats live. The local store reads this install's database; a server-backed store can
/// replace it later without changes to the services or UI, so nothing here exposes EF types.
/// </summary>
public interface ISocialStore
{
    /// <summary>A unit of work. Writes are only kept after <see cref="ISocialSession.CommitAsync"/>, and reads and
    /// writes in one session see a consistent state, so a check-then-write can't race another click.</summary>
    Task<ISocialSession> OpenAsync();

    /// <summary>Brings the users' cached points and streaks up to date before they're read. A streak can break at
    /// midnight without anything being logged, so a friend who hasn't opened the app would otherwise show a stale one.
    /// Call it outside a session.</summary>
    Task RefreshStatsAsync(IReadOnlyCollection<int> userIds, DateOnly today);

    /// <summary>
    /// Gym check-ins of the user's accepted friends from <paramref name="since"/> on (inclusive): only friends who share
    /// them (<c>ShareGymCheckIns == true</c>; not asked yet means no) and only logs with a gym. Gym and day only, never
    /// a time; a custom gym never comes with coordinates. A read, so it needs no session.
    /// </summary>
    Task<IReadOnlyList<FriendCheckIn>> GetFriendGymCheckInsAsync(int userId, DateOnly since);
}

public interface ISocialSession : IAsyncDisposable
{
    Task<SocialUser?> FindUserAsync(int userId);

    /// <summary>Users whose username starts with <paramref name="prefix"/> (ignoring case), by username.</summary>
    Task<IReadOnlyList<SocialUser>> SearchUsersAsync(string prefix, int excludeUserId, int limit);

    /// <summary>Every friendship row involving the user, pending or accepted.</summary>
    Task<IReadOnlyList<FriendLink>> GetFriendshipsAsync(int userId);

    /// <summary>The row for the pair, with <paramref name="userAId"/> &lt; <paramref name="userBId"/>.</summary>
    Task<FriendshipRecord?> GetFriendshipAsync(int userAId, int userBId);

    Task AddFriendshipAsync(FriendshipRecord friendship);
    Task UpdateFriendshipAsync(FriendshipRecord friendship);
    Task DeleteFriendshipAsync(int userAId, int userBId);

    /// <summary>Public stats for each user that exists. <see cref="PublicStats.WeekPoints"/> sums the points dated
    /// <paramref name="weekStart"/> to <paramref name="weekEnd"/>, both included.</summary>
    Task<IReadOnlyList<PublicStats>> GetPublicStatsAsync(IReadOnlyCollection<int> userIds, DateOnly weekStart, DateOnly weekEnd);

    /// <summary>Keeps the writes. Throws <see cref="SocialStoreConflictException"/> if another write got there first.</summary>
    Task CommitAsync();
}

/// <summary><see cref="ISocialStore"/> on this install's SQLite database. Friends work between accounts on the same install.</summary>
public class LocalSocialStore(IDbContextFactory<AppDbContext> dbFactory) : ISocialStore
{
    public async Task<ISocialSession> OpenAsync()
    {
        var db = await dbFactory.CreateDbContextAsync();
        // SQLite takes the write lock up front (BEGIN IMMEDIATE), so two sessions' check-then-write run one after the other
        var tx = await db.Database.BeginTransactionAsync();
        return new Session(db, tx);
    }

    /// <summary>Replays each user's logs, like their own app would: the streak first, then the points. Both are
    /// idempotent, so running them for a friend whose numbers are already current changes nothing.</summary>
    public async Task RefreshStatsAsync(IReadOnlyCollection<int> userIds, DateOnly today)
    {
        var streaks = new StreakService(dbFactory);
        var points = new PointsService(dbFactory);
        foreach (var id in userIds)
        {
            await streaks.RecalculateAsync(id, today);
            await points.SyncAsync(id, today);
        }
    }

    public async Task<IReadOnlyList<FriendCheckIn>> GetFriendGymCheckInsAsync(int userId, DateOnly since)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var friendIds = db.Friendships
            .Where(f => f.Status == FriendshipStatus.Accepted && (f.UserAId == userId || f.UserBId == userId))
            .Select(f => f.UserAId == userId ? f.UserBId : f.UserAId);

        // The custom-gym rule is enforced here too: a custom gym's position (maybe someone's home) never leaves the store
        return await (
            from log in db.WorkoutLogs.AsNoTracking()
            where log.GymId != null && log.Date >= since && friendIds.Contains(log.UserId)
            join profile in db.UserProfiles on log.UserId equals profile.UserId
            where profile.ShareGymCheckIns == true
            select new FriendCheckIn(
                log.UserId,
                log.User.Username,
                log.Gym!.Id,
                log.Gym.Name,
                log.Gym.CityId,
                log.Gym.City.Name,
                log.Gym.OwnerUserId == null ? log.Gym.Latitude : null,
                log.Gym.OwnerUserId == null ? log.Gym.Longitude : null,
                null,                                   // distance: FriendMapService fills it in
                log.Gym.OwnerUserId != null,
                log.Date)).ToListAsync();
    }

    private sealed class Session(AppDbContext db, IDbContextTransaction tx) : ISocialSession
    {
        public async Task<SocialUser?> FindUserAsync(int userId) =>
            await db.Users.AsNoTracking().Where(u => u.Id == userId)
                .Select(u => new SocialUser(u.Id, u.Username)).FirstOrDefaultAsync();

        public async Task<IReadOnlyList<SocialUser>> SearchUsersAsync(string prefix, int excludeUserId, int limit) =>
            await db.Users.AsNoTracking()
                .Where(u => u.Id != excludeUserId && u.Username.StartsWith(prefix))   // NOCASE column: ignores case
                .OrderBy(u => u.Username)
                .Take(limit)
                .Select(u => new SocialUser(u.Id, u.Username))
                .ToListAsync();

        public async Task<IReadOnlyList<FriendLink>> GetFriendshipsAsync(int userId)
        {
            var rows = await db.Friendships.AsNoTracking()
                .Where(f => f.UserAId == userId || f.UserBId == userId)
                .Select(f => new
                {
                    Row = f,
                    Other = f.UserAId == userId
                        ? new SocialUser(f.UserB.Id, f.UserB.Username)
                        : new SocialUser(f.UserA.Id, f.UserA.Username)
                })
                .ToListAsync();
            return rows.Select(r => new FriendLink(ToRecord(r.Row), r.Other)).ToList();
        }

        public async Task<FriendshipRecord?> GetFriendshipAsync(int userAId, int userBId)
        {
            var row = await Row(userAId, userBId);
            return row is null ? null : ToRecord(row);
        }

        public Task AddFriendshipAsync(FriendshipRecord f)
        {
            db.Friendships.Add(new Friendship
            {
                UserAId = f.UserAId, UserBId = f.UserBId, RequestedById = f.RequestedById,
                Status = f.Status, CreatedAt = f.CreatedAt, AcceptedAt = f.AcceptedAt
            });
            return Task.CompletedTask;
        }

        public async Task UpdateFriendshipAsync(FriendshipRecord f)
        {
            var row = await Row(f.UserAId, f.UserBId)
                      ?? throw new SocialStoreConflictException("The friendship no longer exists.");
            row.Status = f.Status;
            row.AcceptedAt = f.AcceptedAt;
        }

        public async Task DeleteFriendshipAsync(int userAId, int userBId)
        {
            if (await Row(userAId, userBId) is { } row) db.Friendships.Remove(row);
        }

        public async Task<IReadOnlyList<PublicStats>> GetPublicStatsAsync(IReadOnlyCollection<int> userIds, DateOnly weekStart, DateOnly weekEnd)
        {
            var ids = userIds.ToList();
            var week = await db.PointsEntries.AsNoTracking()
                .Where(p => ids.Contains(p.UserId) && p.Date >= weekStart && p.Date <= weekEnd)
                .GroupBy(p => p.UserId)
                .Select(g => new { UserId = g.Key, Points = g.Sum(p => p.Amount) })
                .ToDictionaryAsync(x => x.UserId, x => x.Points);

            var users = await db.Users.AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .Select(u => new { u.Id, u.Username, u.Points, u.CurrentStreak })
                .ToListAsync();
            return users.Select(u => new PublicStats(u.Id, u.Username, u.Points, week.GetValueOrDefault(u.Id), u.CurrentStreak))
                .ToList();
        }

        public async Task CommitAsync()
        {
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException e) { throw new SocialStoreConflictException("Another change got there first.", e); }
            await tx.CommitAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await tx.DisposeAsync();   // rolls back if not committed
            await db.DisposeAsync();
        }

        private Task<Friendship?> Row(int userAId, int userBId) =>
            db.Friendships.FirstOrDefaultAsync(f => f.UserAId == userAId && f.UserBId == userBId);

        private static FriendshipRecord ToRecord(Friendship f) =>
            new(f.UserAId, f.UserBId, f.RequestedById, f.Status, f.CreatedAt, f.AcceptedAt);
    }
}
