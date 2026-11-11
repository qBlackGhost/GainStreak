using GymApp.Core;
using GymApp.Social;

namespace GymApp.Tests.Shared;

/// <summary>
/// The <see cref="FriendService"/> rules, run against every <see cref="ISocialStore"/>: the app's SQLite store
/// (GymApp.Tests) and the server's Postgres store (GymApp.Server.Tests). This file is linked into both projects;
/// each derives from it and fills in the database hooks.
/// </summary>
public abstract class FriendServiceTests : IAsyncLifetime
{
    /// <summary>A clock the test moves by hand, so "newest first" is deterministic.</summary>
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly ManualClock _clock = new();
    private ISocialStore _store = null!;
    private FriendService _svc = null!;
    private int _alice, _bob, _carol;

    /// <summary>Sets up an empty database and returns the store on it.</summary>
    protected abstract Task<ISocialStore> CreateStoreAsync();

    /// <summary>Drops whatever <see cref="CreateStoreAsync"/> set up.</summary>
    protected abstract Task DeleteStoreAsync();

    /// <summary>Adds a user with that username straight into the database and returns its id.</summary>
    protected abstract Task<int> AddUserAsync(string username);

    /// <summary>Deletes the user the way account deletion does, so the database's cascades run.</summary>
    protected abstract Task DeleteUserAsync(int userId);

    /// <summary>Every friendship row, read straight from the database.</summary>
    protected abstract Task<IReadOnlyList<FriendshipRecord>> RowsAsync();

    /// <summary>Inserts friendship rows straight into the database.</summary>
    protected abstract Task SeedFriendshipsAsync(IReadOnlyList<FriendshipRecord> rows);

    public async Task InitializeAsync()
    {
        _store = await CreateStoreAsync();
        _svc = new FriendService(_store, _clock);
        _alice = await AddUserAsync("alice");
        _bob = await AddUserAsync("bob");
        _carol = await AddUserAsync("carol");
    }

    public Task DisposeAsync() => DeleteStoreAsync();

    private async Task MakeFriends(int x, int y)
    {
        Assert.True((await _svc.SendRequestAsync(x, y)).Success);
        Assert.True((await _svc.AcceptAsync(y, x)).Success);
    }

    // ---- block ----

    [Fact] public async Task Block_EndsTheFriendship_AndKeepsThemApart()
    {
        await MakeFriends(_alice, _bob);

        Assert.True((await _svc.BlockAsync(_alice, _bob)).Success);

        Assert.Empty(await _svc.GetFriendsAsync(_alice));
        Assert.Empty(await _svc.GetFriendsAsync(_bob));
        Assert.Empty(await RowsAsync());
        Assert.Equal(["bob"], (await _svc.GetBlockedAsync(_alice)).Select(b => b.Username));
        Assert.Empty(await _svc.GetBlockedAsync(_bob));
    }

    [Fact] public async Task Block_RemovesAPendingRequest_FromEitherSide()
    {
        await _svc.SendRequestAsync(_bob, _alice);
        await _svc.BlockAsync(_alice, _bob);
        Assert.Empty((await _svc.GetRequestsAsync(_alice)).Incoming);
        Assert.Empty((await _svc.GetRequestsAsync(_bob)).Outgoing);

        await _svc.SendRequestAsync(_carol, _bob);
        await _svc.BlockAsync(_carol, _bob);
        Assert.Empty((await _svc.GetRequestsAsync(_bob)).Incoming);
    }

    [Fact] public async Task Blocked_CantRequest_AndLooksLikeAnUnknownUser()
    {
        await _svc.BlockAsync(_alice, _bob);

        var theirs = await _svc.SendRequestAsync(_bob, _alice);
        Assert.False(theirs.Success);
        Assert.Equal(FriendService.UnknownUserError, theirs.Error);

        var mine = await _svc.SendRequestAsync(_alice, _bob);
        Assert.False(mine.Success);
        Assert.NotEqual(FriendService.UnknownUserError, mine.Error);
    }

    [Fact] public async Task Search_HidesBlocks_BothWays()
    {
        await _svc.BlockAsync(_alice, _bob);

        Assert.Empty((await _svc.SearchAsync(_alice, "bob")).Users);
        Assert.Empty((await _svc.SearchAsync(_bob, "alice")).Users);
        Assert.Equal(["bob"], (await _svc.SearchAsync(_carol, "bob")).Users.Select(u => u.Username));
    }

    [Fact] public async Task Unblock_LetsThemMeetAgain_ButFriendshipDoesntComeBack()
    {
        await MakeFriends(_alice, _bob);
        await _svc.BlockAsync(_alice, _bob);

        Assert.True((await _svc.UnblockAsync(_alice, _bob)).Success);

        Assert.Empty(await _svc.GetBlockedAsync(_alice));
        Assert.Empty(await _svc.GetFriendsAsync(_alice));
        Assert.Single((await _svc.SearchAsync(_alice, "bob")).Users);
        Assert.True((await _svc.SendRequestAsync(_bob, _alice)).Success);
    }

    [Fact] public async Task BlockedBothWays_EachUnblockOnlyLiftsTheirOwn()
    {
        await _svc.BlockAsync(_alice, _bob);
        await _svc.BlockAsync(_bob, _alice);

        await _svc.UnblockAsync(_alice, _bob);

        Assert.False((await _svc.SendRequestAsync(_alice, _bob)).Success);
        Assert.Equal(["alice"], (await _svc.GetBlockedAsync(_bob)).Select(b => b.Username));
    }

    [Fact] public async Task Block_Twice_IsFine_AndUnblockingNobodyIsToo()
    {
        Assert.True((await _svc.BlockAsync(_alice, _bob)).Success);
        Assert.True((await _svc.BlockAsync(_alice, _bob)).Success);
        Assert.Single(await _svc.GetBlockedAsync(_alice));
        Assert.True((await _svc.UnblockAsync(_alice, _carol)).Success);
    }

    [Fact] public async Task Block_RefusesYourselfAndUnknownUsers()
    {
        Assert.False((await _svc.BlockAsync(_alice, _alice)).Success);
        Assert.Equal(FriendService.UnknownUserError, (await _svc.BlockAsync(_alice, 999_999)).Error);
    }

    [Fact] public async Task DeletingAUser_RemovesTheirBlocks()
    {
        await _svc.BlockAsync(_alice, _bob);
        await DeleteUserAsync(_bob);
        Assert.Empty(await _svc.GetBlockedAsync(_alice));
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

        var row = Assert.Single(await RowsAsync());
        Assert.Equal(FriendshipStatus.Accepted, row.Status);
        Assert.Equal(_clock.Now.UtcDateTime, row.AcceptedAt);
    }

    [Fact] public async Task Pair_IsStoredOnce_SmallerIdFirst_WhoeverSent()
    {
        Assert.True(_alice < _carol);
        await _svc.SendRequestAsync(_carol, _alice);   // the higher id sends

        var row = Assert.Single(await RowsAsync());
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
        var row = Assert.Single(await RowsAsync());
        Assert.Equal((FriendshipStatus.Accepted, _alice), (row.Status, row.RequestedById));
    }

    [Fact] public async Task SenderCantAcceptTheirOwnRequest()
    {
        await _svc.SendRequestAsync(_alice, _bob);
        var r = await _svc.AcceptAsync(_alice, _bob);

        Assert.False(r.Success);
        Assert.Equal("You can't accept your own request.", r.Error);
        Assert.Equal(FriendshipStatus.Pending, Assert.Single(await RowsAsync()).Status);
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
        Assert.Empty(await RowsAsync());

        Assert.True((await _svc.SendRequestAsync(_alice, _bob)).Success);
        Assert.Single(await RowsAsync());
    }

    [Fact] public async Task Cancel_DeletesTheRow_AndANewRequestWorks()
    {
        await _svc.SendRequestAsync(_alice, _bob);
        Assert.False((await _svc.CancelAsync(_bob, _alice)).Success);    // only the sender cancels
        Assert.True((await _svc.CancelAsync(_alice, _bob)).Success);
        Assert.Empty(await RowsAsync());

        Assert.True((await _svc.SendRequestAsync(_bob, _alice)).Success);
        Assert.Equal(_bob, Assert.Single(await RowsAsync()).RequestedById);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Remove_ByEitherSide(bool bySender)
    {
        await MakeFriends(_alice, _bob);
        var r = bySender ? await _svc.RemoveAsync(_alice, _bob) : await _svc.RemoveAsync(_bob, _alice);

        Assert.True(r.Success);
        Assert.Empty(await RowsAsync());
        Assert.Empty(await _svc.GetFriendsAsync(_alice));
        Assert.Empty(await _svc.GetFriendsAsync(_bob));
    }

    [Fact] public async Task Remove_DoesntCancelAPendingRequest()
    {
        await _svc.SendRequestAsync(_alice, _bob);
        Assert.Equal("You're not friends.", (await _svc.RemoveAsync(_alice, _bob)).Error);
        Assert.Single(await RowsAsync());
    }

    [Fact] public async Task DeclineAndCancel_DontRemoveAFriendship()
    {
        await MakeFriends(_alice, _bob);
        Assert.False((await _svc.DeclineAsync(_bob, _alice)).Success);
        Assert.False((await _svc.CancelAsync(_alice, _bob)).Success);
        Assert.Single(await RowsAsync());
    }

    // ---- refusals and limits ----

    [Fact] public async Task NoRequestToYourself()
    {
        var r = await _svc.SendRequestAsync(_alice, _alice);
        Assert.Equal((false, "You can't add yourself."), (r.Success, r.Error));
        Assert.Empty(await RowsAsync());
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
        Assert.Single(await RowsAsync());
    }

    [Fact] public async Task NoRequestWhenAlreadyFriends()
    {
        await MakeFriends(_alice, _bob);
        Assert.Equal("You're already friends.", (await _svc.SendRequestAsync(_alice, _bob)).Error);
        Assert.Equal("You're already friends.", (await _svc.SendRequestAsync(_bob, _alice)).Error);
    }

    /// <summary>Adds users and friendships with <paramref name="userId"/> straight into the store's database.</summary>
    private async Task SeedFriendsAsync(int userId, int count, string prefix, FriendshipStatus status = FriendshipStatus.Accepted)
    {
        var rows = new List<FriendshipRecord>();
        for (var i = 0; i < count; i++)
        {
            var id = await AddUserAsync($"{prefix}{i:D2}");
            rows.Add(new FriendshipRecord(Math.Min(userId, id), Math.Max(userId, id), userId, status, _clock.Now.UtcDateTime,
                status == FriendshipStatus.Accepted ? _clock.Now.UtcDateTime : null));
        }
        await SeedFriendshipsAsync(rows);
    }

    [Fact] public async Task FriendLimit_OnSend()
    {
        await SeedFriendsAsync(_alice, FriendService.MaxFriends, "pal");
        var r = await _svc.SendRequestAsync(_alice, _bob);
        Assert.Equal((false, "You can have at most 50 friends."), (r.Success, r.Error));
    }

    [Fact] public async Task FriendLimit_OnAccept_ForTheReceiver()
    {
        await _svc.SendRequestAsync(_bob, _alice);
        await SeedFriendsAsync(_alice, FriendService.MaxFriends, "pal");

        var r = await _svc.AcceptAsync(_alice, _bob);
        Assert.Equal((false, "You can have at most 50 friends."), (r.Success, r.Error));
        Assert.Equal(FriendshipStatus.Pending, (await RowsAsync()).Single(f => f.RequestedById == _bob).Status);
    }

    [Fact] public async Task FriendLimit_OnAccept_ForTheSender()
    {
        await _svc.SendRequestAsync(_bob, _alice);
        await SeedFriendsAsync(_bob, FriendService.MaxFriends, "pal");

        var r = await _svc.AcceptAsync(_alice, _bob);
        Assert.Equal((false, "They already have the most friends allowed."), (r.Success, r.Error));
    }

    [Fact] public async Task FriendLimit_OnAutoAccept()
    {
        await _svc.SendRequestAsync(_bob, _alice);
        await SeedFriendsAsync(_alice, FriendService.MaxFriends, "pal");
        Assert.False((await _svc.SendRequestAsync(_alice, _bob)).Success);
    }

    [Fact] public async Task PendingOutgoingLimit()
    {
        await SeedFriendsAsync(_alice, FriendService.MaxPendingOutgoing, "ask", FriendshipStatus.Pending);
        var r = await _svc.SendRequestAsync(_alice, _bob);
        Assert.False(r.Success);
        Assert.StartsWith("You can have at most 20 requests waiting.", r.Error);

        // incoming requests don't count against it
        Assert.True((await _svc.SendRequestAsync(_bob, _carol)).Success);
    }

    // ---- lists ----

    [Fact] public async Task Friends_OrderedByUsername()
    {
        var zed = await AddUserAsync("Zed");
        var ana = await AddUserAsync("ana");
        await MakeFriends(_alice, zed);
        await MakeFriends(_alice, _carol);
        await MakeFriends(ana, _alice);
        await _svc.SendRequestAsync(_alice, _bob);   // pending: not a friend

        Assert.Equal(["ana", "carol", "Zed"], (await _svc.GetFriendsAsync(_alice)).Select(f => f.Username));
    }

    [Fact] public async Task Requests_IncomingAndOutgoing_NewestFirst()
    {
        var dan = await AddUserAsync("dan");
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
        await AddUserAsync("Alicia");
        await AddUserAsync("malice");   // contains "ali" but doesn't start with it

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
        for (var i = 0; i < 25; i++) await AddUserAsync($"lifter{i:D2}");
        var r = await _svc.SearchAsync(_alice, "lifter");
        Assert.Equal(20, r.Users.Count);
        Assert.Equal("lifter00", r.Users[0].Username);
    }

    [Fact] public async Task Search_ShowsTheRelationship()
    {
        var bobby = await AddUserAsync("bobby");
        var bobo = await AddUserAsync("bobo");
        await AddUserAsync("bobcat");
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
        await AddUserAsync("a_bcd");
        await AddUserAsync("axbcd");
        var r = await _svc.SearchAsync(_alice, "a_b");
        Assert.Equal(["a_bcd"], r.Users.Select(u => u.Username));
    }

    // ---- invites (Phase 21) ----

    private async Task<string> CodeOf(int userId)
    {
        var r = await _svc.GetInviteCodeAsync(userId);
        Assert.True(r.Success, r.Error);
        return r.Code!;
    }

    [Fact] public async Task InviteCode_IsMadeOnce_AndKept()
    {
        var code = await CodeOf(_alice);
        Assert.Equal(FriendInvites.CodeLength, code.Length);
        Assert.Equal(code, FriendInvites.Normalize(code));
        Assert.Equal(code, await CodeOf(_alice));
        Assert.NotEqual(code, await CodeOf(_bob));
    }

    [Fact] public async Task InviteCode_Reset_ReplacesIt_AndTheOldOneStopsWorking()
    {
        var old = await CodeOf(_alice);
        var reset = await _svc.ResetInviteCodeAsync(_alice);
        Assert.True(reset.Success);
        Assert.NotEqual(old, reset.Code);
        Assert.Equal(reset.Code, await CodeOf(_alice));

        var r = await _svc.AcceptInviteAsync(_bob, old);
        Assert.Equal((false, FriendService.UnknownInviteError), (r.Success, r.Error));
        Assert.True((await _svc.AcceptInviteAsync(_bob, reset.Code!)).Success);
    }

    [Fact] public async Task InviteCode_ForAUserWhoDoesntExist_IsRefused() =>
        Assert.Equal(FriendService.UnknownUserError, (await _svc.GetInviteCodeAsync(99_999)).Error);

    [Fact] public async Task LookUpInvite_NamesTheInviter()
    {
        var r = await _svc.LookUpInviteAsync(_bob, await CodeOf(_alice));
        Assert.True(r.Success);
        Assert.Equal(new SocialUser(_alice, "alice"), r.Inviter);
        Assert.Equal(FriendRelation.None, r.Relation);
    }

    [Fact] public async Task LookUpInvite_ShowsTheRelation()
    {
        var code = await CodeOf(_alice);
        await _svc.SendRequestAsync(_bob, _alice);
        Assert.Equal(FriendRelation.RequestSent, (await _svc.LookUpInviteAsync(_bob, code)).Relation);
        await _svc.AcceptAsync(_alice, _bob);
        Assert.Equal(FriendRelation.Friend, (await _svc.LookUpInviteAsync(_bob, code)).Relation);

        await _svc.SendRequestAsync(_alice, _carol);
        Assert.Equal(FriendRelation.RequestReceived, (await _svc.LookUpInviteAsync(_carol, code)).Relation);
    }

    [Fact] public async Task LookUpInvite_IgnoresCaseAndSpaces()
    {
        var code = await CodeOf(_alice);
        Assert.True((await _svc.LookUpInviteAsync(_bob, $"  {code.ToUpperInvariant()} ")).Success);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("abcdefghjk2")]        // too long
    [InlineData("abcdefghj0")]         // 0 isn't in the alphabet
    [InlineData("abc%' or 1")]
    public async Task LookUpInvite_MalformedCodes_AreUnknown(string code)
    {
        await CodeOf(_alice);
        var r = await _svc.LookUpInviteAsync(_bob, code);
        Assert.Equal((false, FriendService.UnknownInviteError), (r.Success, r.Error));
    }

    [Fact] public async Task LookUpInvite_AWellFormedCodeNoOneHas_IsUnknown() =>
        Assert.Equal(FriendService.UnknownInviteError, (await _svc.LookUpInviteAsync(_bob, "abcdefghjk")).Error);

    [Fact] public async Task Invite_OwnCode_IsRefused()
    {
        var code = await CodeOf(_alice);
        Assert.Equal(FriendService.OwnInviteError, (await _svc.LookUpInviteAsync(_alice, code)).Error);
        Assert.Equal(FriendService.OwnInviteError, (await _svc.AcceptInviteAsync(_alice, code)).Error);
    }

    [Fact] public async Task AcceptInvite_MakesFriends_WithNoRequestStep()
    {
        var r = await _svc.AcceptInviteAsync(_bob, await CodeOf(_alice));

        Assert.True(r.Success, r.Error);
        Assert.True(r.BecameFriends);
        Assert.Equal(["bob"], (await _svc.GetFriendsAsync(_alice)).Select(f => f.Username));
        Assert.Equal(["alice"], (await _svc.GetFriendsAsync(_bob)).Select(f => f.Username));
        var row = Assert.Single(await RowsAsync());
        Assert.Equal((FriendshipStatus.Accepted, _alice), (row.Status, row.RequestedById));
        Assert.Empty((await _svc.GetRequestsAsync(_alice)).Outgoing);
    }

    [Fact] public async Task AcceptInvite_WhenAlreadyFriends_IsRefused()
    {
        await MakeFriends(_alice, _bob);
        var r = await _svc.AcceptInviteAsync(_bob, await CodeOf(_alice));
        Assert.Equal((false, "You're already friends."), (r.Success, r.Error));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AcceptInvite_AcceptsARequestAlreadyWaiting_FromEitherSide(bool scannerSentIt)
    {
        if (scannerSentIt) await _svc.SendRequestAsync(_bob, _alice);
        else await _svc.SendRequestAsync(_alice, _bob);

        Assert.True((await _svc.AcceptInviteAsync(_bob, await CodeOf(_alice))).Success);

        var row = Assert.Single(await RowsAsync());
        Assert.Equal(FriendshipStatus.Accepted, row.Status);
        Assert.Equal(["bob"], (await _svc.GetFriendsAsync(_alice)).Select(f => f.Username));
    }

    [Fact] public async Task Invite_FromSomeoneWhoBlockedYou_LooksUnknown()
    {
        var code = await CodeOf(_alice);
        await _svc.BlockAsync(_alice, _bob);
        Assert.Equal(FriendService.UnknownInviteError, (await _svc.LookUpInviteAsync(_bob, code)).Error);
        Assert.Equal(FriendService.UnknownInviteError, (await _svc.AcceptInviteAsync(_bob, code)).Error);
        Assert.Empty(await RowsAsync());
    }

    [Fact] public async Task Invite_FromSomeoneYouBlocked_SaysSo()
    {
        var code = await CodeOf(_alice);
        await _svc.BlockAsync(_bob, _alice);
        Assert.Equal("You blocked them. Unblock them first.", (await _svc.AcceptInviteAsync(_bob, code)).Error);
        Assert.Empty(await RowsAsync());
    }

    [Fact] public async Task AcceptInvite_FriendLimit_ForTheScanner()
    {
        var code = await CodeOf(_alice);
        await SeedFriendsAsync(_bob, FriendService.MaxFriends, "pal");
        var r = await _svc.AcceptInviteAsync(_bob, code);
        Assert.Equal((false, "You can have at most 50 friends."), (r.Success, r.Error));
    }

    [Fact] public async Task AcceptInvite_FriendLimit_ForTheInviter()
    {
        var code = await CodeOf(_alice);
        await SeedFriendsAsync(_alice, FriendService.MaxFriends, "pal");
        var r = await _svc.AcceptInviteAsync(_bob, code);
        Assert.Equal((false, "They already have the most friends allowed."), (r.Success, r.Error));
    }

    [Fact] public async Task DeletingAUser_RemovesTheirInviteCode()
    {
        var code = await CodeOf(_alice);
        await DeleteUserAsync(_alice);
        Assert.Equal(FriendService.UnknownInviteError, (await _svc.LookUpInviteAsync(_bob, code)).Error);
    }

    [Fact] public async Task TwoUsersCantHoldTheSameCode()
    {
        var code = await CodeOf(_alice);
        await using var s = await _store.OpenAsync();
        await s.SetInviteCodeAsync(_bob, code, DateTime.UtcNow);
        await Assert.ThrowsAsync<SocialStoreConflictException>(() => s.CommitAsync());
    }

    // ---- account deletion ----

    [Fact] public async Task DeletingAUser_RemovesTheirFriendships()
    {
        await MakeFriends(_alice, _bob);
        await _svc.SendRequestAsync(_carol, _bob);

        await DeleteUserAsync(_bob);

        Assert.Empty(await RowsAsync());
        Assert.Empty(await _svc.GetFriendsAsync(_alice));
        Assert.Empty((await _svc.GetRequestsAsync(_carol)).Outgoing);
    }

    // ---- concurrency ----

    [Fact] public async Task ConflictingWrite_IsReportedNotThrown()
    {
        // Simulate another click winning between the check and the write: the unique index refuses the second row
        await using var s = await _store.OpenAsync();
        var (a, b) = (Math.Min(_alice, _bob), Math.Max(_alice, _bob));
        await s.AddFriendshipAsync(new FriendshipRecord(a, b, _alice, FriendshipStatus.Pending, DateTime.UtcNow, null));
        await s.AddFriendshipAsync(new FriendshipRecord(a, b, _bob, FriendshipStatus.Pending, DateTime.UtcNow, null));
        await Assert.ThrowsAsync<SocialStoreConflictException>(() => s.CommitAsync());
    }
}
