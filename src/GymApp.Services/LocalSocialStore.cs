using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using GymApp.Social;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GymApp.Services;

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

        public async Task<IReadOnlyList<PublicStats>> GetPublicStatsAsync(IReadOnlyCollection<int> userIds, DateOnly weekStart, DateOnly weekEnd, DateOnly today)
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
