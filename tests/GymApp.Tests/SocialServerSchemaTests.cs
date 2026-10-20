using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GymApp.Tests;

/// <summary>Phase 14c: the link to the social server account, its read cache and the pending uploads.</summary>
public class SocialServerSchemaTests
{
    [Fact] public void Migration_AppliesOnAnExistingDatabase_AndLeavesItsDataAlone()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;

        // A database from before Phase 14, with a user and a workout, written in SQL since the model has moved on
        using (var db = new AppDbContext(options))
        {
            db.GetService<IMigrator>().Migrate("AccountEmail");
            db.Database.ExecuteSqlRaw("""
                INSERT INTO Users (Id, Username, Email, PasswordHash, CreatedAt, Points, PointsBackfilled, CurrentStreak, LongestStreak, GoalType)
                VALUES (1, 'alice', 'alice@example.com', 'hash', '2026-10-01 08:00:00', 420, 1, 3, 9, 'Maintain');
                INSERT INTO WorkoutLogs (UserId, Date) VALUES (1, '2026-10-18');
                """);
        }

        using (var db = new AppDbContext(options))
        {
            db.Database.Migrate();
            Assert.Empty(db.Database.GetPendingMigrations());

            var alice = db.Users.Single();
            Assert.Equal(("alice", "alice@example.com", "hash", 420, 3, 9), (alice.Username, alice.Email, alice.PasswordHash,
                alice.Points, alice.CurrentStreak, alice.LongestStreak));
            Assert.Null(alice.ServerUserId);
            Assert.Equal(new DateOnly(2026, 10, 18), db.WorkoutLogs.Single().Date);
            Assert.Empty(db.SocialCache);
            Assert.Empty(db.SyncStates);
        }
    }

    [Fact] public void ServerUserId_IsUnique_ButManyAccountsCanBeUnlinked()
    {
        using var t = new TestDb();
        var a = t.AddUser("alice");
        var b = t.AddUser("bob");
        t.AddUser("carol");   // stays unlinked, like bob at first

        using (var db = t.CreateDbContext())
        {
            db.Users.Single(u => u.Id == a).ServerUserId = 17;
            db.SaveChanges();
        }
        using (var db = t.CreateDbContext())
        {
            db.Users.Single(u => u.Id == b).ServerUserId = 17;
            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        }
        using (var db = t.CreateDbContext())
            Assert.Equal(2, db.Users.Count(u => u.ServerUserId == null));
    }

    [Fact] public void CacheKey_IsOneRowPerUserAndKey()
    {
        using var t = new TestDb();
        var a = t.AddUser("alice");
        var b = t.AddUser("bob");
        using (var db = t.CreateDbContext())
        {
            db.SocialCache.Add(new SocialCache { UserId = a, Key = SocialCacheKeys.Friends, Json = "[]", FetchedAt = DateTime.UtcNow });
            db.SocialCache.Add(new SocialCache { UserId = b, Key = SocialCacheKeys.Friends, Json = "[]", FetchedAt = DateTime.UtcNow });
            db.SaveChanges();
        }
        using (var db = t.CreateDbContext())
        {
            db.SocialCache.Add(new SocialCache { UserId = a, Key = SocialCacheKeys.Friends, Json = "[1]", FetchedAt = DateTime.UtcNow });
            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        }
    }

    [Fact] public void DeletingAUser_RemovesTheirCacheAndSyncRows_Only()
    {
        using var t = new TestDb();
        var a = t.AddUser("alice");
        var b = t.AddUser("bob");
        using (var db = t.CreateDbContext())
        {
            foreach (var id in new[] { a, b })
            {
                db.SocialCache.Add(new SocialCache { UserId = id, Key = SocialCacheKeys.Friends, Json = "[]", FetchedAt = DateTime.UtcNow });
                db.SocialCache.Add(new SocialCache { UserId = id, Key = SocialCacheKeys.LeaderboardAllTime, Json = "{}", FetchedAt = DateTime.UtcNow });
                db.SyncStates.Add(new SyncState { UserId = id, StatsDirtySince = DateTime.UtcNow });
            }
            db.SaveChanges();
        }

        using (var db = t.CreateDbContext())
        {
            db.Users.Where(u => u.Id == a).ExecuteDelete();
            Assert.Equal([b, b], db.SocialCache.Select(c => c.UserId).ToList());
            Assert.Equal(b, db.SyncStates.Single().UserId);
        }
    }
}
