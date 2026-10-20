using GymApp.Server.Data;
using GymApp.Server.Friends;
using GymApp.Social;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Server.Tests;

/// <summary>The shared <see cref="GymApp.Tests.Shared.FriendServiceTests"/> cases on the server's <see cref="PostgresSocialStore"/>,
/// each on a fresh database on the shared container. No HTTP here; the endpoints have their own tests.</summary>
[Collection(ServerCollection.Name)]
public sealed class PostgresFriendServiceTests(PostgresFixture postgres) : GymApp.Tests.Shared.FriendServiceTests
{
    private readonly DbContextOptions<ServerDbContext> _options = new DbContextOptionsBuilder<ServerDbContext>()
        .UseNpgsql(postgres.ConnectionString($"test_{Guid.NewGuid():N}")).Options;

    private sealed class Factory(DbContextOptions<ServerDbContext> options) : IDbContextFactory<ServerDbContext>
    {
        public ServerDbContext CreateDbContext() => new(options);
    }

    private ServerDbContext Db() => new(_options);

    protected override async Task<ISocialStore> CreateStoreAsync()
    {
        await using (var db = Db())
            await db.Database.MigrateAsync();
        return new PostgresSocialStore(new Factory(_options));
    }

    protected override async Task DeleteStoreAsync()
    {
        await using var db = Db();
        await db.Database.EnsureDeletedAsync();
    }

    protected override async Task<int> AddUserAsync(string username)
    {
        await using var db = Db();
        var user = new User
        {
            Username = username,
            UsernameNormalized = User.NormalizeUsername(username),
            Email = $"{username.ToLowerInvariant()}@example.com",
            PasswordHash = "x",   // never signs in; a real hash would cost 210k iterations per user
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    protected override async Task DeleteUserAsync(int userId)
    {
        await using var db = Db();
        await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync();
    }

    protected override async Task<IReadOnlyList<FriendshipRecord>> RowsAsync()
    {
        await using var db = Db();
        return await db.Friendships.AsNoTracking()
            .Select(f => new FriendshipRecord(f.UserAId, f.UserBId, f.RequestedById, f.Status, f.CreatedAt, f.AcceptedAt))
            .ToListAsync();
    }

    protected override async Task SeedFriendshipsAsync(IReadOnlyList<FriendshipRecord> rows)
    {
        await using var db = Db();
        db.Friendships.AddRange(rows.Select(f => new Friendship
        {
            UserAId = f.UserAId, UserBId = f.UserBId, RequestedById = f.RequestedById,
            Status = f.Status, CreatedAt = f.CreatedAt, AcceptedAt = f.AcceptedAt
        }));
        await db.SaveChangesAsync();
    }
}
