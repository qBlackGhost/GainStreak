using GymApp.Client;
using GymApp.Contracts;

namespace GymApp.Server.Tests;

/// <summary>The app's <see cref="SocialApiClient"/> against the real server, so Contracts, the server and the client stay in step.</summary>
public class ClientContractTests : ServerTest
{
    private static readonly DateOnly Today = new(2026, 10, 14);   // a Wednesday

    public ClientContractTests(PostgresFixture postgres) : base(postgres) =>
        Clock.Fixed = new DateTimeOffset(Today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

    /// <summary>Registers through the client and keeps its token, as the app does.</summary>
    private async Task<(SocialApiClient Api, MeResponse Me)> RegisterAsync(string username)
    {
        var tokens = new MemoryTokenStore();
        var api = Api(tokens);
        var r = await api.RegisterAsync(new RegisterRequest(username, $"{username}@example.com", DefaultPassword, "phone"));
        Assert.True(r.IsOk, r.Error);
        tokens.Set(r.Value!.Token);
        return (api, r.Value.User);
    }

    private static StatsUploadRequest Stats(int total, int week, int streak) =>
        new([new StatsDayRequest(Today, total, week, streak)]);

    [Fact] public async Task Register_Login_Me_AndAccountChanges()
    {
        var (alice, me) = await RegisterAsync("alice");
        Assert.Equal(new MeResponse(me.Id, "alice", "alice@example.com", null), (await alice.GetMeAsync()).Value);

        var taken = await Api().RegisterAsync(new RegisterRequest("ALICE", "x@example.com", DefaultPassword, "phone"));
        Assert.Equal((409, "That username is taken.", ProblemFields.Username), (taken.Status, taken.Detail, taken.Field));

        var wrong = await Api().LoginAsync(new LoginRequest("alice", "wrong password", "tablet"));
        Assert.Equal((401, "Wrong username or password."), (wrong.Status, wrong.Error));

        var badEmail = await alice.ChangeEmailAsync(new ChangeEmailRequest(DefaultPassword, "alice@example.com"));
        Assert.Equal((400, "That is already your email.", ProblemFields.Email), (badEmail.Status, badEmail.Detail, badEmail.Field));
        Assert.True((await alice.ChangeEmailAsync(new ChangeEmailRequest(DefaultPassword, "new@example.com"))).IsOk);
        Assert.True((await alice.ChangePasswordAsync(new ChangePasswordRequest(DefaultPassword, "brand new pass"))).IsOk);

        var login = await Api().LoginAsync(new LoginRequest("new@example.com", "brand new pass", "tablet"));
        Assert.Equal(me.Id, login.Value!.User.Id);

        Assert.True((await alice.LogoutAsync()).IsOk);
        var expired = 0;
        alice.SessionExpired += (_, _) => expired++;
        Assert.Equal(401, (await alice.GetMeAsync()).Status);
        Assert.Equal(1, expired);
    }

    [Fact] public async Task ChangeUsername_ReturnsTheAccount_AndRefusalsCarryTheirField()
    {
        var (alice, me) = await RegisterAsync("alice");
        await RegisterAsync("bobby");

        var taken = await alice.ChangeUsernameAsync(new ChangeUsernameRequest("BOBBY", DefaultPassword));
        Assert.Equal((409, "That username is taken.", ProblemFields.Username), (taken.Status, taken.Detail, taken.Field));
        var wrong = await alice.ChangeUsernameAsync(new ChangeUsernameRequest("alice2", "wrong password"));
        Assert.Equal((400, ProblemFields.Password), (wrong.Status, wrong.Field));

        var renamed = await alice.ChangeUsernameAsync(new ChangeUsernameRequest("alice2", DefaultPassword));
        var allowedAt = Clock.Fixed!.Value.UtcDateTime + GymApp.Core.Credentials.UsernameChangeCooldown;
        Assert.Equal(me with { Username = "alice2", UsernameChangeAllowedAt = allowedAt }, renamed.Value);
        Assert.Equal(allowedAt, (await alice.GetMeAsync()).Value!.UsernameChangeAllowedAt);

        var tooSoon = await alice.ChangeUsernameAsync(new ChangeUsernameRequest("alice3", DefaultPassword));
        Assert.Equal((429, "You can change your username again on 13 November 2026.", ProblemFields.Username),
            (tooSoon.Status, tooSoon.Detail, tooSoon.Field));
    }

    [Fact] public async Task PushDevice_RegisterAndUnregister()
    {
        var (alice, _) = await RegisterAsync("alice");
        Assert.True((await alice.RegisterPushDeviceAsync(new PushDeviceRequest(PushPlatform.Fcm, "fcm:token-1"))).IsOk);
        Assert.True((await alice.RegisterPushDeviceAsync(new PushDeviceRequest(PushPlatform.Apns, "abcd1234", Sandbox: true))).IsOk);
        await using (var db = Db())
        {
            var device = Assert.Single(db.PushDevices);
            Assert.Equal((PushPlatform.Apns, "abcd1234", true), (device.Platform, device.Token, device.Sandbox));
        }

        var bad = await alice.RegisterPushDeviceAsync(new PushDeviceRequest(PushPlatform.Apns, "has space"));
        Assert.Equal((400, PushRules.BadTokenMessage), (bad.Status, bad.Error));
        Assert.True((await alice.UnregisterPushDeviceAsync()).IsOk);
        await using (var db = Db()) Assert.Empty(db.PushDevices);
    }

    [Fact] public async Task DeleteAccount_NeedsThePassword()
    {
        var (alice, _) = await RegisterAsync("alice");

        var wrong = await alice.DeleteAccountAsync(new DeleteAccountRequest("wrong password"));
        Assert.Equal(("That password is not right.", ProblemFields.Password), (wrong.Error, wrong.Field));
        Assert.True((await alice.DeleteAccountAsync(new DeleteAccountRequest(DefaultPassword))).IsOk);
        Assert.False((await Api().LoginAsync(new LoginRequest("alice", DefaultPassword, "phone"))).IsOk);
    }

    [Fact] public async Task FullFriendFlow()
    {
        var (alice, aliceMe) = await RegisterAsync("alice");
        var (bob, bobMe) = await RegisterAsync("bob");
        var (carol, carolMe) = await RegisterAsync("carol");

        var tooShort = await alice.SearchAsync("b");
        Assert.Equal((400, $"Type at least {GymApp.Social.FriendService.MinSearchLength} characters."), (tooShort.Status, tooShort.Error));
        var found = Assert.Single((await alice.SearchAsync("BOB")).Value!);
        Assert.Equal(new FoundUserResponse(bobMe.Id, "bob", FriendRelation.None), found);

        Assert.False((await alice.SendRequestAsync(bobMe.Id)).Value!.BecameFriends);
        Assert.Equal(FriendRelation.RequestSent, Assert.Single((await alice.SearchAsync("bob")).Value!).Relation);
        Assert.Equal(1, (await bob.GetIncomingCountAsync()).Value!.Count);
        var requests = (await bob.GetRequestsAsync()).Value!;
        Assert.Equal("alice", Assert.Single(requests.Incoming).Username);
        Assert.Empty(requests.Outgoing);

        // Only the receiver accepts; the server's message comes through
        var refused = await alice.AcceptAsync(bobMe.Id);
        Assert.True(refused.IsRefused);
        Assert.NotNull(refused.Detail);

        Assert.True((await bob.AcceptAsync(aliceMe.Id)).IsOk);
        Assert.Equal(["bob"], (await alice.GetFriendsAsync()).Value!.Select(f => f.Username));
        Assert.Equal(["alice"], (await bob.GetFriendsAsync()).Value!.Select(f => f.Username));

        // Carol: a request sent the other way round becomes a friendship
        await carol.SendRequestAsync(aliceMe.Id);
        Assert.True((await alice.SendRequestAsync(carolMe.Id)).Value!.BecameFriends);

        // Decline, cancel, remove
        await bob.SendRequestAsync(carolMe.Id);
        Assert.True((await carol.DeclineAsync(bobMe.Id)).IsOk);
        await bob.SendRequestAsync(carolMe.Id);
        Assert.True((await bob.CancelRequestAsync(carolMe.Id)).IsOk);
        Assert.Equal(0, (await carol.GetIncomingCountAsync()).Value!.Count);
        Assert.True((await alice.RemoveFriendAsync(bobMe.Id)).IsOk);
        Assert.Equal(["carol"], (await alice.GetFriendsAsync()).Value!.Select(f => f.Username));

        var unknown = await alice.SendRequestAsync(99999);
        Assert.Equal(404, unknown.Status);
    }

    [Fact] public async Task InviteFlow()
    {
        var (alice, aliceMe) = await RegisterAsync("alice");
        var (bob, bobMe) = await RegisterAsync("bob");

        var invite = (await alice.GetInviteAsync()).Value!;
        Assert.EndsWith($"/add/{invite.Code}", invite.Url);
        var reset = (await alice.ResetInviteAsync()).Value!;
        Assert.NotEqual(invite.Code, reset.Code);

        var old = await bob.LookUpInviteAsync(invite.Code);
        Assert.Equal((404, GymApp.Social.FriendService.UnknownInviteError), (old.Status, old.Error));

        Assert.Equal(new InviteLookupResponse(aliceMe.Id, "alice", FriendRelation.None), (await bob.LookUpInviteAsync(reset.Code)).Value);
        Assert.True((await bob.AcceptInviteAsync(reset.Code)).Value!.BecameFriends);
        Assert.Equal(["bob"], (await alice.GetFriendsAsync()).Value!.Select(f => f.Username));
        Assert.Equal(FriendRelation.Friend, (await bob.LookUpInviteAsync(reset.Code)).Value!.Relation);

        var own = await alice.AcceptInviteAsync(reset.Code);
        Assert.Equal((400, GymApp.Social.FriendService.OwnInviteError), (own.Status, own.Error));
        Assert.NotEqual(aliceMe.Id, bobMe.Id);
    }

    [Fact] public async Task Avatar_ShowsOnMeAndTheLeaderboard_AndUnknownIsRefused()
    {
        var (alice, _) = await RegisterAsync("alice");
        Assert.True((await alice.SetAvatarAsync("teal:flame")).IsOk);
        Assert.Equal("teal:flame", (await alice.GetMeAsync()).Value!.Avatar);
        Assert.True((await alice.UploadStatsAsync(Stats(100, 10, 1))).IsOk);
        Assert.Equal("teal:flame", (await alice.GetLeaderboardAsync(LeaderboardPeriod.AllTime, Today)).Value!.Rows[0].Avatar);

        Assert.True((await alice.SetAvatarAsync("beige")).IsRefused);
        Assert.True((await alice.SetAvatarAsync(null)).IsOk);
        Assert.Null((await alice.GetMeAsync()).Value!.Avatar);
    }

    [Fact] public async Task StatsUpload_AndLeaderboard()
    {
        var (alice, aliceMe) = await RegisterAsync("alice");
        var (bob, bobMe) = await RegisterAsync("bob");
        await alice.SendRequestAsync(bobMe.Id);
        await bob.AcceptAsync(aliceMe.Id);

        Assert.True((await alice.UploadStatsAsync(Stats(1200, 150, 4))).IsOk);
        Assert.True((await bob.UploadStatsAsync(Stats(900, 300, 2))).IsOk);
        var refused = await alice.UploadStatsAsync(new StatsUploadRequest([]));
        Assert.Equal("Send at least one day.", refused.Error);

        var week = (await alice.GetLeaderboardAsync(LeaderboardPeriod.ThisWeek, Today)).Value!;
        Assert.Equal(LeaderboardPeriod.ThisWeek, week.Period);
        Assert.True(week.WeekStart <= Today && Today <= week.WeekEnd);
        Assert.Equal([("bob", 300, false), ("alice", 150, true)], week.Rows.Select(r => (r.Username, r.Points, r.IsYou)));

        var allTime = (await alice.GetLeaderboardAsync(LeaderboardPeriod.AllTime, Today)).Value!;
        Assert.Equal([("alice", 1200, 4), ("bob", 900, 2)], allTime.Rows.Select(r => (r.Username, r.Points, r.CurrentStreak)));
        Assert.Equal([1, 2], allTime.Rows.Select(r => r.Rank));
    }

    [Fact] public async Task CheckIns_AndFriendsCheckIns()
    {
        var (alice, aliceMe) = await RegisterAsync("alice");
        var (bob, bobMe) = await RegisterAsync("bob");
        await alice.SendRequestAsync(bobMe.Id);
        await bob.AcceptAsync(aliceMe.Id);

        var gym = new CheckInRequest(OsmType.Way, 101, "World Class", OsmType.Relation, 7, "Timișoara", 45.75, 21.23);
        var notSharing = await bob.PutCheckInAsync(Today, gym);
        Assert.Equal((403, "Turn on sharing gym check-ins first."), (notSharing.Status, notSharing.Error));

        Assert.True((await bob.SetSharingAsync(true)).IsOk);
        Assert.True((await bob.PutCheckInAsync(Today, gym)).IsOk);
        var c = Assert.Single((await alice.GetFriendCheckInsAsync(Today.AddDays(-7))).Value!);
        Assert.Equal(new FriendCheckInResponse(bobMe.Id, "bob", Today, OsmType.Way, 101, "World Class",
            OsmType.Relation, 7, "Timișoara", 45.75, 21.23), c);

        Assert.True((await bob.ReplaceCheckInsAsync(new CheckInsUploadRequest([
            new DatedCheckInRequest(Today.AddDays(-1), null, null, "Garage gym", OsmType.Relation, 7, "Timișoara", 45.7, 21.2)
        ]))).IsOk);
        c = Assert.Single((await alice.GetFriendCheckInsAsync(Today.AddDays(-7))).Value!);
        Assert.Equal(("Garage gym", null, null, null), (c.GymName, c.GymOsmId, c.Latitude, c.Longitude));

        Assert.True((await bob.DeleteCheckInAsync(Today.AddDays(-1))).IsOk);
        Assert.Empty((await alice.GetFriendCheckInsAsync(Today.AddDays(-7))).Value!);
        Assert.True((await bob.SetSharingAsync(false)).IsOk);
        Assert.False((await bob.GetMeAsync()).Value!.ShareGymCheckIns);
    }
}
