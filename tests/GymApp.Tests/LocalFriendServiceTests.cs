using GymApp.Data.Models;
using GymApp.Services;
using GymApp.Social;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

/// <summary>The shared <see cref="Shared.FriendServiceTests"/> cases on the app's SQLite store.</summary>
public sealed class LocalFriendServiceTests : Shared.FriendServiceTests
{
    private readonly TestDb _db = new();

    protected override Task<ISocialStore> CreateStoreAsync() => Task.FromResult<ISocialStore>(new LocalSocialStore(_db));

    protected override Task DeleteStoreAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    protected override Task<int> AddUserAsync(string username) => Task.FromResult(_db.AddUser(username));

    protected override async Task DeleteUserAsync(int userId)
    {
        await using var db = _db.CreateDbContext();
        db.Users.Remove(await db.Users.SingleAsync(u => u.Id == userId));
        await db.SaveChangesAsync();
    }

    protected override async Task<IReadOnlyList<FriendshipRecord>> RowsAsync()
    {
        await using var db = _db.CreateDbContext();
        return await db.Friendships.AsNoTracking()
            .Select(f => new FriendshipRecord(f.UserAId, f.UserBId, f.RequestedById, f.Status, f.CreatedAt, f.AcceptedAt))
            .ToListAsync();
    }

    protected override async Task SeedFriendshipsAsync(IReadOnlyList<FriendshipRecord> rows)
    {
        await using var db = _db.CreateDbContext();
        db.Friendships.AddRange(rows.Select(f => new Friendship
        {
            UserAId = f.UserAId, UserBId = f.UserBId, RequestedById = f.RequestedById,
            Status = f.Status, CreatedAt = f.CreatedAt, AcceptedAt = f.AcceptedAt
        }));
        await db.SaveChangesAsync();
    }
}
