using System.Net;
using GymApp.Contracts;
using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;
using GymApp.Social;
using Microsoft.EntityFrameworkCore;
using CorePeriod = GymApp.Core.LeaderboardPeriod;

namespace GymApp.Tests;

public sealed class RemoteSocialServicesTests : IDisposable
{
    private static readonly DateTime Since = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 10, 14);

    private readonly TestDb _db = new();
    private readonly FakeSocialServer _server = new();
    private readonly SessionService _session = new();
    private readonly RemoteFriendService _friends;
    private readonly RemoteLeaderboardService _board;
    private readonly int _me;

    public RemoteSocialServicesTests()
    {
        _me = _db.AddUser("alice");
        using (var db = _db.CreateDbContext())
            _session.SignIn(db.Users.Single(u => u.Id == _me));
        var cache = new SocialCacheStore(_db);
        _friends = new RemoteFriendService(_server.Api, cache, _session);
        _board = new RemoteLeaderboardService(_server.Api, cache, _session);
    }

    public void Dispose() => _db.Dispose();

    private void ServeFriends() =>
        _server.On("GET v1/friends", HttpStatusCode.OK, new List<FriendResponse> { new(901, "bob", Since), new(902, "carol", Since) });

    private List<SocialCache> CacheRows()
    {
        using var db = _db.CreateDbContext();
        return db.SocialCache.AsNoTracking().ToList();
    }

    // --- Reads and the cache ---

    [Fact]
    public async Task Blocks_GoThroughTheServer_AndOfflineListsNobody()
    {
        _server.On("GET v1/blocks", HttpStatusCode.OK, new List<BlockedUserResponse> { new(901, "bob", DateTime.UtcNow) });
        _server.On("PUT v1/blocks/902", HttpStatusCode.NoContent);
        _server.On("DELETE v1/blocks/901", HttpStatusCode.NoContent);

        Assert.Equal(["bob"], (await _friends.GetBlockedAsync(_me)).Select(b => b.Username));
        Assert.True((await _friends.BlockAsync(_me, 902)).Success);
        Assert.True((await _friends.UnblockAsync(_me, 901)).Success);

        _server.Offline = true;
        Assert.Empty(await _friends.GetBlockedAsync(_me));
        Assert.False((await _friends.BlockAsync(_me, 902)).Success);
    }

    [Fact] public async Task OnlineRead_IsFresh_AndSavedToTheCache()
    {
        ServeFriends();

        var friends = await _friends.GetFriendsAsync(_me);

        Assert.Equal([(901, "bob"), (902, "carol")], friends.Select(f => (f.UserId, f.Username)));
        var cached = Assert.IsAssignableFrom<ICachedResult>(friends);
        Assert.False(cached.IsStale);
        Assert.Equal((_me, SocialCacheKeys.Friends), (CacheRows().Single().UserId, CacheRows().Single().Key));
    }

    [Fact] public async Task OfflineRead_ReturnsTheCachedData_MarkedStale()
    {
        ServeFriends();
        await _friends.GetFriendsAsync(_me);
        var fetchedAt = CacheRows().Single().FetchedAt;

        _server.Offline = true;
        var friends = await _friends.GetFriendsAsync(_me);

        Assert.Equal(["bob", "carol"], friends.Select(f => f.Username));
        var cached = (ICachedResult)friends;
        Assert.True(cached.IsStale);
        Assert.Equal(fetchedAt, cached.FetchedAt);
    }

    [Fact] public async Task OfflineRead_WithNothingCached_IsEmptyAndStale()
    {
        _server.Offline = true;

        var friends = (FriendList)await _friends.GetFriendsAsync(_me);
        var requests = await _friends.GetRequestsAsync(_me);
        var board = await _board.GetAsync(_me, CorePeriod.ThisWeek, Today);

        Assert.Empty(friends);
        Assert.Equal((true, null), (friends.IsStale, friends.FetchedAt));
        Assert.Equal((true, 0, 0), (requests.IsStale, requests.Incoming.Count, requests.Outgoing.Count));
        Assert.Equal((true, 0), (board!.IsStale, board.Rows.Count));
        Assert.Equal(new DateOnly(2026, 10, 12), board.WeekStart);
    }

    [Fact] public async Task Requests_AndTheIncomingCount_UseTheCacheOffline()
    {
        _server.On("GET v1/friends/requests", HttpStatusCode.OK, new FriendRequestsResponse(
            [new(903, "dan", Since), new(904, "eve", Since)], [new(905, "fay", Since)]));
        _server.On("GET v1/friends/requests/count", HttpStatusCode.OK, new CountResponse(2));
        var fresh = await _friends.GetRequestsAsync(_me);
        Assert.Equal(["dan", "eve"], fresh.Incoming.Select(r => r.Username));
        Assert.Equal(["fay"], fresh.Outgoing.Select(r => r.Username));
        Assert.Equal(2, await _friends.IncomingCountAsync(_me));

        _server.Offline = true;
        var stale = await _friends.GetRequestsAsync(_me);
        Assert.True(stale.IsStale);
        Assert.Equal(2, stale.Incoming.Count);
        Assert.Equal(2, await _friends.IncomingCountAsync(_me));
    }

    [Fact] public async Task CacheIsPerUser()
    {
        ServeFriends();
        await _friends.GetFriendsAsync(_me);

        var bobId = _db.AddUser("bob");
        using (var db = _db.CreateDbContext())
            _session.SignIn(db.Users.Single(u => u.Id == bobId));
        _server.Offline = true;

        Assert.Empty(await _friends.GetFriendsAsync(bobId));
    }

    // --- Writes ---

    [Fact] public async Task OfflineWrite_ReturnsTheOfflineMessage_AndChangesNoCache()
    {
        ServeFriends();
        await _friends.GetFriendsAsync(_me);
        var before = CacheRows().Single();

        _server.Offline = true;
        var send = await _friends.SendRequestAsync(_me, 950);
        var remove = await _friends.RemoveAsync(_me, 901);

        Assert.Equal((false, "You're offline. Try again when you're connected."), (send.Success, send.Error));
        Assert.Equal("You're offline. Try again when you're connected.", remove.Error);
        var after = CacheRows().Single();
        Assert.Equal((before.Json, before.FetchedAt), (after.Json, after.FetchedAt));
    }

    [Fact] public async Task Refusals_CarryTheServersMessage_And409IsTheConflictMessage()
    {
        _server.On("POST v1/friends/901/accept", HttpStatusCode.BadRequest, FakeSocialServer.Problem(400, "You can't accept your own request."));
        _server.On("POST v1/friends/902/accept", HttpStatusCode.Conflict, FakeSocialServer.Problem(409, "Conflict."));
        _server.On("POST v1/friends/903/request", HttpStatusCode.OK, new SendRequestResponse(true));

        Assert.Equal("You can't accept your own request.", (await _friends.AcceptAsync(_me, 901)).Error);
        Assert.Equal(FriendService.ConflictError, (await _friends.AcceptAsync(_me, 902)).Error);
        var became = await _friends.SendRequestAsync(_me, 903);
        Assert.Equal((true, true), (became.Success, became.BecameFriends));
    }

    [Fact] public async Task Search_MapsRelations_AndAShortQuerySendsNothing()
    {
        _server.On("GET v1/users/search", HttpStatusCode.OK, new List<FoundUserResponse>
        {
            new(901, "bob", GymApp.Contracts.FriendRelation.Friend),
            new(902, "bobby", GymApp.Contracts.FriendRelation.RequestReceived)
        });

        var r = await _friends.SearchAsync(_me, " bob ");
        Assert.Equal([GymApp.Social.FriendRelation.Friend, GymApp.Social.FriendRelation.RequestReceived], r.Users.Select(u => u.Relation));

        var tooShort = await _friends.SearchAsync(_me, "bo");
        Assert.Equal((false, "Type at least 3 characters."), (tooShort.Success, tooShort.Error));
        Assert.Single(_server.Calls);

        _server.Offline = true;
        Assert.Equal("You're offline. Try again when you're connected.", (await _friends.SearchAsync(_me, "bob")).Error);
    }

    [Fact] public async Task OnlyTheSignedInUser_CanBeAskedFor()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _friends.GetFriendsAsync(_me + 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _board.GetAsync(_me + 1, CorePeriod.AllTime, Today));
        _session.SignOut();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _friends.SendRequestAsync(_me, 901));
        Assert.Empty(_server.Calls);
    }

    // --- Leaderboard ---

    [Fact] public async Task Leaderboard_SendsTheDate_RebuildsLevels_AndCachesPerPeriod()
    {
        var response = new LeaderboardResponse(GymApp.Contracts.LeaderboardPeriod.ThisWeek, new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 18),
        [
            new(1, 901, "bob", 300, 4, "Dedicated", 5, false),
            new(2, 41, "alice", 150, 2, "Novice", 3, true)
        ]);
        string? query = null;
        _server.On("GET v1/leaderboard", req => { query = req.RequestUri!.Query; return FakeSocialServer.Respond(HttpStatusCode.OK, response); });

        var view = (await _board.GetAsync(_me, CorePeriod.ThisWeek, Today))!;

        Assert.Equal("?period=thisWeek&today=2026-10-14", query);
        Assert.False(view.IsStale);
        Assert.Equal((2, "Novice"), (view.You!.Level.Number, view.You.Level.Name));
        Assert.Equal(("Dedicated", 1500), (view.Rows[0].Level.Name, view.Rows[0].Level.NextFrom));
        Assert.Equal([SocialCacheKeys.LeaderboardThisWeek], CacheRows().Select(c => c.Key));

        _server.Offline = true;
        var stale = (await _board.GetAsync(_me, CorePeriod.ThisWeek, Today))!;
        Assert.True(stale.IsStale);
        Assert.Equal(["bob", "alice"], stale.Rows.Select(r => r.Username));
        Assert.Empty((await _board.GetAsync(_me, CorePeriod.AllTime, Today))!.Rows);   // not cached for all time
    }

    [Fact] public void LevelForNumber_MatchesTheTable()
    {
        Assert.Equal(LevelCalculator.For(700) with { Progress = 0 }, LevelCalculator.ForNumber(4));
        Assert.Equal(LevelCalculator.For(99999), LevelCalculator.ForNumber(7));
        Assert.Equal(1, LevelCalculator.ForNumber(0).Number);
        Assert.Equal(7, LevelCalculator.ForNumber(42).Number);
    }
}
