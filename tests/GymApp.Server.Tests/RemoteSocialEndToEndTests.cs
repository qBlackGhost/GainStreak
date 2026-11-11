using GymApp.Contracts;
using GymApp.Services;
using GymApp.Social;
using CorePeriod = GymApp.Core.LeaderboardPeriod;
using FriendRelation = GymApp.Social.FriendRelation;

namespace GymApp.Server.Tests;

/// <summary>The app's remote friend and leaderboard services against the real server, one <see cref="Phone"/> per user.</summary>
public class RemoteSocialEndToEndTests : ServerTest
{
    private static readonly DateOnly Today = new(2026, 10, 14);   // a Wednesday

    public RemoteSocialEndToEndTests(PostgresFixture postgres) : base(postgres) =>
        Clock.Fixed = new DateTimeOffset(Today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

    [Fact] public async Task FullFriendFlow()
    {
        var alice = await PhoneAsync("alice");
        var bob = await PhoneAsync("bob");

        var found = await alice.Friends.SearchAsync(alice.Me, "bob");
        var bobRow = Assert.Single(found.Users);
        Assert.Equal((bob.ServerId, FriendRelation.None), (bobRow.UserId, bobRow.Relation));

        Assert.True((await alice.Friends.SendRequestAsync(alice.Me, bobRow.UserId)).Success);
        Assert.Equal(1, await bob.Friends.IncomingCountAsync(bob.Me));
        var incoming = Assert.Single((await bob.Friends.GetRequestsAsync(bob.Me)).Incoming);
        Assert.Equal(("alice", alice.ServerId), (incoming.Username, incoming.UserId));
        Assert.Equal("bob", Assert.Single((await alice.Friends.GetRequestsAsync(alice.Me)).Outgoing).Username);

        Assert.True((await bob.Friends.AcceptAsync(bob.Me, incoming.UserId)).Success);
        var friends = await alice.Friends.GetFriendsAsync(alice.Me);
        Assert.Equal("bob", Assert.Single(friends).Username);
        Assert.False(((ICachedResult)friends).IsStale);
        Assert.Equal(FriendRelation.Friend, Assert.Single((await alice.Friends.SearchAsync(alice.Me, "bob")).Users).Relation);

        Assert.True((await bob.Friends.RemoveAsync(bob.Me, alice.ServerId)).Success);
        Assert.Empty(await alice.Friends.GetFriendsAsync(alice.Me));

        // Decline and cancel
        await alice.Friends.SendRequestAsync(alice.Me, bob.ServerId);
        Assert.True((await bob.Friends.DeclineAsync(bob.Me, alice.ServerId)).Success);
        await alice.Friends.SendRequestAsync(alice.Me, bob.ServerId);
        Assert.True((await alice.Friends.CancelAsync(alice.Me, bob.ServerId)).Success);
        Assert.Equal(0, await bob.Friends.IncomingCountAsync(bob.Me));
    }

    [Fact] public async Task InviteByQrCode_OnePhoneShows_TheOtherAccepts()
    {
        var alice = await PhoneAsync("alice");
        var bob = await PhoneAsync("bob");

        var invite = await alice.Friends.GetInviteCodeAsync(alice.Me);
        Assert.True(invite.Success, invite.Error);
        var code = GymApp.Social.InviteLink.TryParse(new Uri(GymApp.Social.InviteLink.For(invite)));
        Assert.Equal(invite.Code, code);

        var lookup = await bob.Friends.LookUpInviteAsync(bob.Me, code!);
        Assert.Equal((alice.ServerId, "alice", FriendRelation.None), (lookup.Inviter!.Id, lookup.Inviter.Username, lookup.Relation));
        Assert.True((await bob.Friends.AcceptInviteAsync(bob.Me, code!)).BecameFriends);

        Assert.Equal(["bob"], (await alice.Friends.GetFriendsAsync(alice.Me)).Select(f => f.Username));
        Assert.Empty((await alice.Friends.GetRequestsAsync(alice.Me)).Incoming);
    }

    [Fact] public async Task RefusedAccept_CarriesTheServersMessage()
    {
        var alice = await PhoneAsync("alice");
        var bob = await PhoneAsync("bob");
        await alice.Friends.SendRequestAsync(alice.Me, bob.ServerId);

        var own = await alice.Friends.AcceptAsync(alice.Me, bob.ServerId);
        Assert.Equal((false, "You can't accept your own request."), (own.Success, own.Error));

        var none = await bob.Friends.AcceptAsync(bob.Me, 99999);
        Assert.Equal("There's no request to accept.", none.Error);
        var twice = await alice.Friends.SendRequestAsync(alice.Me, bob.ServerId);
        Assert.Equal("You already sent a request.", twice.Error);
    }

    [Fact] public async Task Leaderboard_WithThreeFriends_AndIsYouOnTheRightRow()
    {
        var alice = await PhoneAsync("alice");
        var bob = await PhoneAsync("bob");
        var carol = await PhoneAsync("carol");
        var dan = await PhoneAsync("dan");
        foreach (var friend in new[] { bob, carol, dan })
        {
            await alice.Friends.SendRequestAsync(alice.Me, friend.ServerId);
            await friend.Friends.AcceptAsync(friend.Me, alice.ServerId);
        }

        async Task Upload(Phone p, int total, int week, int streak) =>
            Assert.True((await p.Api.UploadStatsAsync(new StatsUploadRequest([new StatsDayRequest(Today, total, week, streak)]))).IsOk);
        await Upload(alice, 800, 120, 4);
        await Upload(bob, 1600, 90, 9);
        await Upload(carol, 300, 200, 1);
        await Upload(dan, 50, 0, 0);

        var week = (await alice.Board.GetAsync(alice.Me, CorePeriod.ThisWeek, Today))!;
        Assert.False(week.IsStale);
        Assert.Equal(["carol", "alice", "bob", "dan"], week.Rows.Select(r => r.Username));
        Assert.Equal(alice.ServerId, Assert.Single(week.Rows, r => r.IsYou).UserId);
        Assert.Equal(2, week.You!.Rank);

        var allTime = (await alice.Board.GetAsync(alice.Me, CorePeriod.AllTime, Today))!;
        Assert.Equal(["bob", "alice", "carol", "dan"], allTime.Rows.Select(r => r.Username));
        Assert.Equal(("Athlete", 9), (allTime.Rows[0].Level.Name, allTime.Rows[0].CurrentStreak));
        Assert.Equal(4, allTime.You!.Level.Number);   // 800 points: Dedicated

        // Bob's board has only Alice: friends of friends never show
        var bobs = (await bob.Board.GetAsync(bob.Me, CorePeriod.AllTime, Today))!;
        Assert.Equal(["bob", "alice"], bobs.Rows.Select(r => r.Username));
        Assert.True(bobs.Rows[0].IsYou);
    }
}
