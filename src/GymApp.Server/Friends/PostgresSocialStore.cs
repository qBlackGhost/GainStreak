using System.Data;
using GymApp.Core;
using GymApp.Server.Data;
using GymApp.Social;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace GymApp.Server.Friends;

/// <summary>
/// <see cref="ISocialStore"/> on the server's Postgres, so the shared <see cref="FriendService"/> rules run here too.
/// Each session is a Serializable transaction: if two requests' check-then-write overlap, one of them fails at the write
/// or the commit and <see cref="FriendService"/> reports its usual "That request just changed". A suspended account
/// (<see cref="User.SuspendedAt"/>) reads as gone: it isn't found or listed, though its rows stay for <c>unsuspend</c>.
/// </summary>
public class PostgresSocialStore(IDbContextFactory<ServerDbContext> dbFactory) : ISocialStore
{
    public async Task<ISocialSession> OpenAsync()
    {
        var db = await dbFactory.CreateDbContextAsync();
        try
        {
            var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            return new Session(db, tx);
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    /// <summary>Nothing to do: phones upload their projected stats (13f), the server never replays logs.</summary>
    public Task RefreshStatsAsync(IReadOnlyCollection<int> userIds, DateOnly today) => Task.CompletedTask;

    /// <summary>Not served through the store: <see cref="FriendCheckIn"/> carries this phone's local gym and city ids,
    /// which mean nothing on the server. Check-ins get their own endpoint, keyed by OSM ids (13g).</summary>
    public Task<IReadOnlyList<FriendCheckIn>> GetFriendGymCheckInsAsync(int userId, DateOnly since) =>
        throw new NotSupportedException("The server serves check-ins from /v1/friends/checkins, keyed by OSM ids.");

    /// <summary>A serialization failure or a unique violation: another transaction got there first.</summary>
    private static bool IsConflict(Exception? e) => e switch
    {
        null => false,
        PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation } => true,
        _ => IsConflict(e.InnerException)
    };

    private sealed class Session(ServerDbContext db, IDbContextTransaction tx) : ISocialSession
    {
        public async Task<SocialUser?> FindUserAsync(int userId) =>
            await db.Users.AsNoTracking().Where(u => u.Id == userId && u.SuspendedAt == null)
                .Select(u => new SocialUser(u.Id, u.Username)).FirstOrDefaultAsync();

        public async Task<IReadOnlyList<SocialUser>> SearchUsersAsync(string prefix, IReadOnlyCollection<int> excludeUserIds, int limit)
        {
            // A prefix match on the lowercase name, with LIKE's wildcards taken literally
            var pattern = User.NormalizeUsername(prefix).Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";
            var excluded = excludeUserIds.ToList();
            return await db.Users.AsNoTracking()
                .Where(u => u.SuspendedAt == null && !excluded.Contains(u.Id) && EF.Functions.Like(u.UsernameNormalized, pattern, @"\"))
                .OrderBy(u => u.UsernameNormalized).ThenBy(u => u.Username)
                .Take(limit)
                .Select(u => new SocialUser(u.Id, u.Username))
                .ToListAsync();
        }

        public async Task<IReadOnlyList<FriendLink>> GetFriendshipsAsync(int userId)
        {
            var rows = await (
                from f in db.Friendships.AsNoTracking()
                where f.UserAId == userId || f.UserBId == userId
                join u in db.Users on (f.UserAId == userId ? f.UserBId : f.UserAId) equals u.Id
                where u.SuspendedAt == null
                select new { Row = f, u.Id, u.Username }).ToListAsync();
            return rows.Select(r => new FriendLink(ToRecord(r.Row), new SocialUser(r.Id, r.Username))).ToList();
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

        public async Task<IReadOnlyList<BlockRecord>> GetBlocksAsync(int userId)
        {
            var rows = await (
                from x in db.UserBlocks.AsNoTracking()
                where x.BlockerId == userId || x.BlockedId == userId
                join u in db.Users on (x.BlockerId == userId ? x.BlockedId : x.BlockerId) equals u.Id
                select new { Row = x, u.Id, u.Username }).ToListAsync();
            return rows.Select(r => new BlockRecord(r.Row.BlockerId, r.Row.BlockedId, r.Row.CreatedAt, new SocialUser(r.Id, r.Username))).ToList();
        }

        public Task AddBlockAsync(int blockerId, int blockedId, DateTime at)
        {
            db.UserBlocks.Add(new UserBlock { BlockerId = blockerId, BlockedId = blockedId, CreatedAt = at });
            return Task.CompletedTask;
        }

        public async Task DeleteBlockAsync(int blockerId, int blockedId)
        {
            if (await db.UserBlocks.FirstOrDefaultAsync(x => x.BlockerId == blockerId && x.BlockedId == blockedId) is { } row)
                db.UserBlocks.Remove(row);
        }

        /// <summary>
        /// Each user's latest uploaded day on or before <paramref name="today"/> (the viewer's date). Its week points
        /// count only if that day is in <paramref name="weekStart"/>..<paramref name="weekEnd"/>; an older row still gives
        /// the total and the streak its projection reached (0 once a training day passed). No upload at all shows 0.
        /// </summary>
        public async Task<IReadOnlyList<PublicStats>> GetPublicStatsAsync(IReadOnlyCollection<int> userIds, DateOnly weekStart, DateOnly weekEnd, DateOnly today)
        {
            var ids = userIds.ToList();
            var latest = await db.ProjectedStats.AsNoTracking()
                .Where(d => ids.Contains(d.UserId) && d.Date <= today)
                .GroupBy(d => d.UserId)
                .Select(g => g.OrderByDescending(d => d.Date).First())
                .ToDictionaryAsync(d => d.UserId);

            var users = await db.Users.AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .Select(u => new { u.Id, u.Username, u.Avatar })
                .ToListAsync();
            return users.Select(u => latest.TryGetValue(u.Id, out var d)
                    ? new PublicStats(u.Id, u.Username, d.TotalPoints,
                        d.Date >= weekStart && d.Date <= weekEnd ? d.WeekPoints : 0, d.CurrentStreak, u.Avatar)
                    : new PublicStats(u.Id, u.Username, 0, 0, 0, u.Avatar))
                .ToList();
        }

        public async Task CommitAsync()
        {
            try
            {
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (Exception e) when (IsConflict(e))
            {
                throw new SocialStoreConflictException("Another change got there first.", e);
            }
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
