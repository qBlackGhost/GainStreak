using GymApp.Core;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

/// <summary>Checks the Friends migration: one row per pair, the status stored as text, and the cascade from both users.</summary>
public class FriendsSchemaTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly int _alice;
    private readonly int _bob;

    public FriendsSchemaTests()
    {
        _alice = _db.AddUser("alice");
        _bob = _db.AddUser("bob");
    }

    public void Dispose() => _db.Dispose();

    private Friendship Pair(FriendshipStatus status = FriendshipStatus.Pending) =>
        new() { UserAId = _alice, UserBId = _bob, RequestedById = _alice, Status = status };

    [Fact] public void SamePair_CanOnlyExistOnce()
    {
        using (var db = _db.CreateDbContext())
        {
            db.Friendships.Add(Pair());
            db.SaveChanges();
        }
        using var again = _db.CreateDbContext();
        again.Friendships.Add(Pair(FriendshipStatus.Accepted));
        Assert.Throws<DbUpdateException>(() => again.SaveChanges());
    }

    [Fact] public void Status_IsStoredAsText()
    {
        using (var db = _db.CreateDbContext())
        {
            db.Friendships.Add(Pair(FriendshipStatus.Accepted));
            db.SaveChanges();
        }
        using var read = _db.CreateDbContext();
        var raw = read.Database.SqlQueryRaw<string>("SELECT Status AS Value FROM Friendships").Single();
        Assert.Equal("Accepted", raw);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DeletingEitherUser_RemovesTheFriendship(bool deleteA)
    {
        using (var db = _db.CreateDbContext())
        {
            db.Friendships.Add(Pair(FriendshipStatus.Accepted));
            db.SaveChanges();
        }
        using (var db = _db.CreateDbContext())
        {
            db.Users.Remove(db.Users.Single(u => u.Id == (deleteA ? _alice : _bob)));
            db.SaveChanges();
        }
        using var read = _db.CreateDbContext();
        Assert.Empty(read.Friendships);
    }
}
