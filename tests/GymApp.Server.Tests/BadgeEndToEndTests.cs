using GymApp.Core;
using CorePeriod = GymApp.Core.LeaderboardPeriod;

namespace GymApp.Server.Tests;

/// <summary>Phase 25d: a badge earned on one phone reaches a friend's phone through the server.</summary>
public class BadgeEndToEndTests(PostgresFixture postgres) : ServerTest(postgres)
{
    [Fact] public async Task ABadgeAndTheFeaturedOne_GoUpWithTheStats_AndAFriendSeesThem()
    {
        var alice = await PhoneAsync("alice");
        var bob = await PhoneAsync("bob");
        await alice.Friends.SendRequestAsync(alice.Me, bob.ServerId);
        await bob.Friends.AcceptAsync(bob.Me, alice.ServerId);
        var today = AppClock.Today;

        await alice.Badges.AwardAsync(alice.Me, "streak", 3, today);
        await alice.Badges.SetFeaturedAsync(alice.Me, "streak");
        alice.Sync.MarkStatsDirty(alice.Me);
        await alice.Sync.WhenIdleAsync();
        await alice.Sync.SyncAsync(alice.Me);

        var seen = (await bob.BadgeShare.GetFriendBadgesAsync(bob.Me, alice.ServerId))!;
        Assert.False(seen.IsStale);
        Assert.Equal(("streak", 3), (Assert.Single(seen.Badges).Id, seen.Badges[0].Tier));
        Assert.Equal("streak", seen.Featured);

        var board = (await bob.Board.GetAsync(bob.Me, CorePeriod.ThisWeek, today))!;
        Assert.Equal("streak:3", board.Rows.Single(r => r.Username == "alice").FeaturedBadge);
    }

    [Fact] public async Task SharingOff_GoesUpToo()
    {
        var alice = await PhoneAsync("alice");
        var bob = await PhoneAsync("bob");
        await alice.Friends.SendRequestAsync(alice.Me, bob.ServerId);
        await bob.Friends.AcceptAsync(bob.Me, alice.ServerId);

        await alice.Badges.AwardAsync(alice.Me, "century", 1);
        await alice.Badges.SetShareAsync(alice.Me, false);
        alice.Sync.MarkStatsDirty(alice.Me);
        await alice.Sync.WhenIdleAsync();
        await alice.Sync.SyncAsync(alice.Me);

        var seen = (await bob.BadgeShare.GetFriendBadgesAsync(bob.Me, alice.ServerId))!;
        Assert.Empty(seen.Badges);
        Assert.Null(seen.Featured);
    }

    [Fact] public async Task NotAFriend_IsNull()
    {
        var alice = await PhoneAsync("alice");
        var bob = await PhoneAsync("bob");
        Assert.Null(await bob.BadgeShare.GetFriendBadgesAsync(bob.Me, alice.ServerId));
    }
}
