using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

public class FriendServiceTests : IDisposable
{
    /// <summary>A clock the test moves by hand, so "newest first" is deterministic.</summary>
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly TestDb _db = new();
    private readonly ManualClock _clock = new();
    private readonly FriendService _svc;
    private readonly int _alice, _bob, _carol;

    public FriendServiceTests()
    {
        _svc = new FriendService(new LocalSocialStore(_db), _clock);
        _alice = _db.AddUser("alice");
        _bob = _db.AddUser("bob");
        _carol = _db.AddUser("carol");
    }

    public void Dispose() => _db.Dispose();

    private List<Friendship> Rows()
    {
        using var db = _db.CreateDbContext();
        return db.Friendships.AsNoTracking().ToList();
    }

    private async Task MakeFriends(int x, int y)
    {
        Assert.True((await _svc.SendRequestAsync(x, y)).Success);
        Assert.True((await _svc.AcceptAsync(y, x)).Success);
    }

    // ---- send / accept ----

    [Fact] public async Task SendThenAccept_BothSeeEachOther()
    {
        var sent = await _svc.SendRequestAsync(_alice, _bob);
        Assert.True(sent.Success);
        Assert.False(sent.BecameFriends);
        Assert.Empty(await _svc.GetFriendsAsync(_alice));

        var accepted = await _svc.AcceptAsync(_bob, _alice);
        Assert.True(accepted.Success);
        Assert.True(accepted.BecameFriends);

        Assert.Equal(["bob"], (await _svc.GetFriendsAsync(_alice)).Select(f => f.Username));
        Assert.Equal(["alice"], (await _svc.GetFriendsAsync(_bob)).Select(f => f.Username));

        var row = Assert.Single(Rows());
        Assert.Equal(FriendshipStatus.Accepted, row.Status);
        Assert.Equal(_clock.Now.UtcDateTime, row.AcceptedAt);
    }

    [Fact] public async Task Pair_IsStoredOnce_SmallerIdFirst_WhoeverSent()
    {
        Assert.True(_alice < _carol);
        await _svc.SendRequestAsync(_carol, _alice);   // the higher id sends

        var row = Assert.Single(Rows());
        Assert.Equal((_alice, _carol, _carol), (row.UserAId, row.UserBId, row.RequestedById));
        Assert.Equal(FriendshipStatus.Pending, row.Status);
        Assert.Null(row.AcceptedAt);
    }

    [Fact] public async Task SendingToSomeoneWhoAlreadySentYouOne_AcceptsIt()
    {
        await _svc.SendRequestAsync(_alice, _bob);
        var r = await _svc.SendRequestAsync(_bob, _alice);

        Assert.True(r.Success);
        Assert.True(r.BecameFriends);
        var row = Assert.Single(Rows());
        Assert.Equal((FriendshipStatus.Accepted, _alice), (row.Status, row.RequestedById));
    }

    [Fact] public async Task SenderCantAcceptTheirOwnRequest()
    {
        await _svc.SendRequestAsync(_alice, _bob);
        var r = await _svc.AcceptAsync(_alice, _bob);

        Assert.False(r.Success);
        Assert.Equal("You can't accept your own request.", r.Error);
        Assert.Equal(FriendshipStatus.Pending, Assert.Single(Rows()).Status);
    }

    [Fact] public async Task AcceptWithoutARequest_Fails()
    {
        var r = await _svc.AcceptAsync(_bob, _alice);
        Assert.Equal((false, "There's no request to accept."), (r.Success, r.Error));
    }

    // ---- decline / cancel / remove ----

    [Fact] public async Task Decline_DeletesTheRow_AndANewRequestWorks()
    {
        await _svc.SendRequestAsync(_alice, _bob);
        Assert.False((await _svc.DeclineAsync(_alice, _bob)).Success);   // only the receiver declines
        Assert.True((await _svc.DeclineAsync(_bob, _alice)).Success);
        Assert.Empty(Rows());

        Assert.True((await _svc.SendRequestAsync(_alice, _bob)).Success);
        Assert.Single(Rows());
    }

    [Fact] public async Task Cancel_DeletesTheRow_AndANewRequestWorks()
    {
        await _svc.SendRequestAsync(_alice, _bob);
        Assert.False((await _svc.CancelAsync(_bob, _alice)).Success);    // only the sender cancels
        Assert.True((await _svc.CancelAsync(_alice, _bob)).Success);
        Assert.Empty(Rows());

        Assert.True((await _svc.SendRequestAsync(_bob, _alice)).Success);
        Assert.Equal(_bob, Assert.Single(Rows()).RequestedById);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Remove_ByEitherSide(bool bySender)
    {
        await MakeFriends(_alice, _bob);
        var r = bySender ? await _svc.RemoveAsync(_alice, _bob) : await _svc.RemoveAsync(_bob, _alice);

        Assert.True(r.Success);
        Assert.Empty(Rows());
        Assert.Empty(await _svc.GetFriendsAsync(_alice));
        Assert.Empty(await _svc.GetFriendsAsync(_bob));
    }

    [Fact] public async Task Remove_DoesntCancelAPendingRequest()
    {
        await _svc.SendRequestAsync(_alice, _bob);
        Assert.Equal("You're not friends.", (await _svc.RemoveAsync(_alice, _bob)).Error);
        Assert.Single(Rows());
    }

    [Fact] public async Task DeclineAndCancel_DontRemoveAFriendship()
    {
        await MakeFriends(_alice, _bob);
        Assert.False((await _svc.DeclineAsync(_bob, _alice)).Success);
        Assert.False((await _svc.CancelAsync(_alice, _bob)).Success);
        Assert.Single(Rows());
    }

    // ---- refusals and limits ----

    [Fact] public async Task NoRequestToYourself()
    {
        var r = await _svc.SendRequestAsync(_alice, _alice);
        Assert.Equal((false, "You can't add yourself."), (r.Success, r.Error));
        Assert.Empty(Rows());
    }

    [Fact] public async Task NoRequestToAMissingUser()
    {
        var r = await _svc.SendRequestAsync(_alice, 9999);
        Assert.Equal((false, "That user doesn't exist."), (r.Success, r.Error));
    }

    [Fact] public async Task NoDuplicateRequests()
    {
        await _svc.SendRequestAsync(_alice, _bob);
        var r = await _svc.SendRequestAsync(_alice, _bob);
        Assert.Equal((false, "You already sent a request."), (r.Success, r.Error));
        Assert.Single(Rows());
    }

    [Fact] public async Task NoRequestWhenAlreadyFriends()
    {
        await MakeFriends(_alice, _bob);
        Assert.Equal("You're already friends.", (await _svc.SendRequestAsync(_alice, _bob)).Error);
        Assert.Equal("You're already friends.", (await _svc.SendRequestAsync(_bob, _alice)).Error);
    }

    /// <summary>Adds users and accepted friendships for <paramref name="userId"/> straight into the database.</summary>
    private void SeedFriends(int userId, int count, string prefix, FriendshipStatus status = FriendshipStatus.Accepted)
    {
        var ids = Enumerable.Range(0, count).Select(i => _db.AddUser($"{prefix}{i:D2}")).ToList();
        using var db = _db.CreateDbContext();
        foreach (var id in ids)
            db.Friendships.Add(new Friendship
            {
                UserAId = Math.Min(userId, id), UserBId = Math.Max(userId, id), RequestedById = userId, Status = status,
                AcceptedAt = status == FriendshipStatus.Accepted ? DateTime.UtcNow : null
            });
        db.SaveChanges();
    }

    [Fact] public async Task FriendLimit_OnSend()
    {
        SeedFriends(_alice, FriendService.MaxFriends, "pal");
        var r = await _svc.SendRequestAsync(_alice, _bob);
        Assert.Equal((false, "You can have at most 50 friends."), (r.Success, r.Error));
    }

    [Fact] public async Task FriendLimit_OnAccept_ForTheReceiver()
    {
        await _svc.SendRequestAsync(_bob, _alice);
        SeedFriends(_alice, FriendService.MaxFriends, "pal");

        var r = await _svc.AcceptAsync(_alice, _bob);
        Assert.Equal((false, "You can have at most 50 friends."), (r.Success, r.Error));
        Assert.Equal(FriendshipStatus.Pending, Rows().Single(f => f.RequestedById == _bob).Status);
    }

    [Fact] public async Task FriendLimit_OnAccept_ForTheSender()
    {
        await _svc.SendRequestAsync(_bob, _alice);
        SeedFriends(_bob, FriendService.MaxFriends, "pal");

        var r = await _svc.AcceptAsync(_alice, _bob);
        Assert.Equal((false, "They already have the most friends allowed."), (r.Success, r.Error));
    }

    [Fact] public async Task FriendLimit_OnAutoAccept()
    {
        await _svc.SendRequestAsync(_bob, _alice);
        SeedFriends(_alice, FriendService.MaxFriends, "pal");
        Assert.False((await _svc.SendRequestAsync(_alice, _bob)).Success);
    }

    [Fact] public async Task PendingOutgoingLimit()
    {
        SeedFriends(_alice, FriendService.MaxPendingOutgoing, "ask", FriendshipStatus.Pending);
        var r = await _svc.SendRequestAsync(_alice, _bob);
        Assert.False(r.Success);
        Assert.StartsWith("You can have at most 20 requests waiting.", r.Error);

        // incoming requests don't count against it
        Assert.True((await _svc.SendRequestAsync(_bob, _carol)).Success);
    }

    // ---- lists ----

    [Fact] public async Task Friends_OrderedByUsername()
    {
        var zed = _db.AddUser("Zed");
        var ana = _db.AddUser("ana");
        await MakeFriends(_alice, zed);
        await MakeFriends(_alice, _carol);
        await MakeFriends(ana, _alice);
        await _svc.SendRequestAsync(_alice, _bob);   // pending: not a friend

        Assert.Equal(["ana", "carol", "Zed"], (await _svc.GetFriendsAsync(_alice)).Select(f => f.Username));
    }

    [Fact] public async Task Requests_IncomingAndOutgoing_NewestFirst()
    {
        var dan = _db.AddUser("dan");
        await _svc.SendRequestAsync(_bob, _alice);
        _clock.Now = _clock.Now.AddMinutes(1);
        await _svc.SendRequestAsync(_carol, _alice);
        _clock.Now = _clock.Now.AddMinutes(1);
        await _svc.SendRequestAsync(_alice, dan);

        var r = await _svc.GetRequestsAsync(_alice);
        Assert.Equal(["carol", "bob"], r.Incoming.Select(x => x.Username));
        Assert.Equal(["dan"], r.Outgoing.Select(x => x.Username));
        Assert.Equal(2, await _svc.IncomingCountAsync(_alice));
        Assert.Equal(1, await _svc.IncomingCountAsync(dan));   // alice's request
    }

    [Fact] public async Task AcceptedRequest_LeavesTheRequestLists()
    {
        await MakeFriends(_bob, _alice);
        var r = await _svc.GetRequestsAsync(_alice);
        Assert.Empty(r.Incoming);
        Assert.Empty(r.Outgoing);
        Assert.Equal(0, await _svc.IncomingCountAsync(_alice));
    }

    // ---- search ----

    [Fact] public async Task Search_PrefixIgnoringCase_LeavesYouOut()
    {
        _db.AddUser("Alicia");
        _db.AddUser("malice");   // contains "ali" but doesn't start with it

        var r = await _svc.SearchAsync(_alice, "ALI");
        Assert.True(r.Success);
        Assert.Equal(["Alicia"], r.Users.Select(u => u.Username));
    }

    [Theory]
    [InlineData("")]
    [InlineData("al")]
    [InlineData("  al  ")]
    public async Task Search_NeedsThreeCharacters(string query)
    {
        var r = await _svc.SearchAsync(_alice, query);
        Assert.False(r.Success);
        Assert.Equal("Type at least 3 characters.", r.Error);
        Assert.Empty(r.Users);
    }

    [Fact] public async Task Search_AtMost20Results()
    {
        for (var i = 0; i < 25; i++) _db.AddUser($"lifter{i:D2}");
        var r = await _svc.SearchAsync(_alice, "lifter");
        Assert.Equal(20, r.Users.Count);
        Assert.Equal("lifter00", r.Users[0].Username);
    }

    [Fact] public async Task Search_ShowsTheRelationship()
    {
        var bobby = _db.AddUser("bobby");
        var bobo = _db.AddUser("bobo");
        _db.AddUser("bobcat");
        await MakeFriends(_alice, _bob);
        await _svc.SendRequestAsync(_alice, bobby);
        await _svc.SendRequestAsync(bobo, _alice);

        var r = await _svc.SearchAsync(_alice, "bob");
        Assert.Equal(
            [("bob", FriendRelation.Friend), ("bobby", FriendRelation.RequestSent),
             ("bobcat", FriendRelation.None), ("bobo", FriendRelation.RequestReceived)],
            r.Users.Select(u => (u.Username, u.Relation)));
    }

    [Fact] public async Task Search_WildcardsAreLiteral()
    {
        _db.AddUser("a_bcd");
        _db.AddUser("axbcd");
        var r = await _svc.SearchAsync(_alice, "a_b");
        Assert.Equal(["a_bcd"], r.Users.Select(u => u.Username));
    }

    // ---- account deletion ----

    [Fact] public async Task DeletingAUser_RemovesTheirFriendships()
    {
        await MakeFriends(_alice, _bob);
        await _svc.SendRequestAsync(_carol, _bob);

        using (var db = _db.CreateDbContext())
        {
            db.Users.Remove(db.Users.Single(u => u.Id == _bob));
            db.SaveChanges();
        }

        Assert.Empty(Rows());
        Assert.Empty(await _svc.GetFriendsAsync(_alice));
        Assert.Empty((await _svc.GetRequestsAsync(_carol)).Outgoing);
    }

    // ---- concurrency ----

    [Fact] public async Task ConflictingWrite_IsReportedNotThrown()
    {
        // Simulate another click winning between the check and the write: the unique index refuses the second row
        await using var s = await new LocalSocialStore(_db).OpenAsync();
        var (a, b) = (Math.Min(_alice, _bob), Math.Max(_alice, _bob));
        await s.AddFriendshipAsync(new FriendshipRecord(a, b, _alice, FriendshipStatus.Pending, DateTime.UtcNow, null));
        await s.AddFriendshipAsync(new FriendshipRecord(a, b, _bob, FriendshipStatus.Pending, DateTime.UtcNow, null));
        await Assert.ThrowsAsync<SocialStoreConflictException>(() => s.CommitAsync());
    }
}
