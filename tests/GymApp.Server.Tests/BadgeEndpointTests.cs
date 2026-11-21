using System.Net;
using System.Net.Http.Json;
using GymApp.Contracts;

namespace GymApp.Server.Tests;

/// <summary>Phase 25d: badges on the server.</summary>
public class BadgeEndpointTests : ServerTest
{
    /// <summary>The server's date in these tests: a Wednesday, so the week runs Monday 12 to Sunday 18 October.</summary>
    private static readonly DateOnly Wednesday = new(2026, 10, 14);
    private static readonly DateOnly LastSunday = new(2026, 10, 11);

    public BadgeEndpointTests(PostgresFixture postgres) : base(postgres) =>
        Clock.Fixed = new DateTimeOffset(Wednesday.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

    private static Task<HttpResponseMessage> UploadAsync(SignedIn user, BadgesUploadRequest request) =>
        user.Client.PutAsJsonAsync("/v1/me/badges", request, ApiFormat.Json);

    private static async Task UploadOkAsync(SignedIn user, string? featured, bool share, params BadgeDto[] badges) =>
        Assert.Equal(HttpStatusCode.NoContent, (await UploadAsync(user, new BadgesUploadRequest(badges, featured, share))).StatusCode);

    private static BadgeDto Badge(string id, int tier = 0) => new(id, tier, new DateOnly(2026, 10, 1));

    private static async Task MakeFriendsAsync(SignedIn a, SignedIn b)
    {
        Assert.Equal(HttpStatusCode.OK, (await a.Client.PostAsync($"/v1/friends/{b.User.Id}/request", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await b.Client.PostAsync($"/v1/friends/{a.User.Id}/accept", null)).StatusCode);
    }

    private static async Task<UserBadgesResponse> FriendBadgesAsync(SignedIn viewer, SignedIn owner)
    {
        var response = await viewer.Client.GetAsync($"/v1/friends/{owner.User.Id}/badges");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UserBadgesResponse>(ApiFormat.Json))!;
    }

    private static async Task<LeaderboardResponse> BoardAsync(SignedIn user, string period)
    {
        var response = await user.Client.GetAsync($"/v1/leaderboard?period={period}&today={Wednesday:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LeaderboardResponse>(ApiFormat.Json))!;
    }

    public static TheoryData<BadgesUploadRequest> BadRequests => new()
    {
        new([new("streak", 1, new(2026, 10, 1)), new("streak", 2, new(2026, 10, 2))], null, true),     // twice
        new([new("streak", 5, new(2026, 10, 1))], null, true),                                         // tier 5
        new([new(new string('x', 41), 0, new(2026, 10, 1))], null, true),                              // too long
        new([new("", 0, new(2026, 10, 1))], null, true),                                               // empty
        new([new("streak", 1, new(2026, 10, 1))], "century", true),                                    // featured not one of them
        new(Enumerable.Range(0, 201).Select(i => new BadgeDto($"b{i}", 0, new(2026, 10, 1))).ToList(), null, true),
    };

    [Theory, MemberData(nameof(BadRequests))]
    public async Task Upload_RefusesBadPayloads(BadgesUploadRequest request)
    {
        var alice = await SignUpAsync("alice");
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(alice, request)).StatusCode);
    }

    [Fact] public async Task AFriend_SeesTheBadgesAndTheFeaturedOne_AndAnUploadReplacesThem()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bob");
        await MakeFriendsAsync(alice, bob);
        await UploadOkAsync(alice, null, true, Badge("century", 1), Badge("on-ice"));
        await UploadOkAsync(alice, "streak", true, Badge("streak", 3), Badge("on-ice"));

        var seen = await FriendBadgesAsync(bob, alice);

        Assert.Equal(alice.User.Id, seen.UserId);
        Assert.Equal(["on-ice", "streak"], seen.Badges.Select(b => b.Id).Order());
        Assert.Equal(3, seen.Badges.Single(b => b.Id == "streak").Tier);
        Assert.Equal("streak", seen.Featured);
    }

    [Fact] public async Task NotAFriend_Is404_EvenWithARequestSent()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bob");
        await UploadOkAsync(alice, null, true, Badge("century", 1));
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.GetAsync($"/v1/friends/{alice.User.Id}/badges")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bob.Client.PostAsync($"/v1/friends/{alice.User.Id}/request", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.GetAsync($"/v1/friends/{alice.User.Id}/badges")).StatusCode);
    }

    [Fact] public async Task SharingOff_ShowsNothing_AnywhereFriendsLook()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bob");
        await MakeFriendsAsync(alice, bob);
        await UploadOkAsync(alice, "streak", false, Badge("streak", 3));

        var seen = await FriendBadgesAsync(bob, alice);
        Assert.Empty(seen.Badges);
        Assert.Null(seen.Featured);
        Assert.Null((await BoardAsync(bob, "allTime")).Rows.Single(r => r.UserId == alice.User.Id).FeaturedBadge);
    }

    [Fact] public async Task TheFeaturedBadge_ShowsOnTheLeaderboard_AsIdAndTier()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bob");
        await MakeFriendsAsync(alice, bob);
        await UploadOkAsync(alice, "heavy-lifter", true, Badge("heavy-lifter", 2));

        var rows = (await BoardAsync(bob, "thisWeek")).Rows;
        Assert.Equal("heavy-lifter:2", rows.Single(r => r.UserId == alice.User.Id).FeaturedBadge);
        Assert.Null(rows.Single(r => r.UserId == bob.User.Id).FeaturedBadge);   // never uploaded
    }

    [Fact] public async Task TheFeaturedBadge_RidesOnFriendCheckIns()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bob");
        await MakeFriendsAsync(alice, bob);
        await UploadOkAsync(alice, "streak", true, Badge("streak", 3));
        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.PutAsJsonAsync("/v1/me/sharing", new SharingRequest(true), ApiFormat.Json)).StatusCode);
        var checkIn = new CheckInRequest(OsmType.Node, 42, "Iron Gym", OsmType.Relation, 7, "Timișoara", 45.75, 21.23);
        Assert.Equal(HttpStatusCode.NoContent,
            (await alice.Client.PutAsJsonAsync($"/v1/me/checkins/{Wednesday:yyyy-MM-dd}", checkIn, ApiFormat.Json)).StatusCode);

        var seen = await bob.Client.GetFromJsonAsync<List<FriendCheckInResponse>>($"/v1/friends/checkins?since={Wednesday:yyyy-MM-dd}", ApiFormat.Json);

        Assert.Equal("streak:3", Assert.Single(seen!).FeaturedBadge);
    }

    [Fact] public async Task LastWeek_ReadsTheSundayBeforeThisWeek_AfterANewProjection()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bob");
        await MakeFriendsAsync(alice, bob);
        // Uploads from last week, then this week's projection starts on Monday
        Clock.Fixed = new DateTimeOffset(LastSunday.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        await alice.Client.PutAsJsonAsync("/v1/me/stats", new StatsUploadRequest([new(LastSunday, 300, 120, 9)]), ApiFormat.Json);
        await bob.Client.PutAsJsonAsync("/v1/me/stats", new StatsUploadRequest([new(LastSunday, 900, 80, 4)]), ApiFormat.Json);
        Clock.Fixed = new DateTimeOffset(Wednesday.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.PutAsJsonAsync("/v1/me/stats",
            new StatsUploadRequest([new(Wednesday, 330, 30, 12)]), ApiFormat.Json)).StatusCode);

        var last = await BoardAsync(alice, "lastWeek");

        Assert.Equal(LeaderboardPeriod.LastWeek, last.Period);
        Assert.Equal((new DateOnly(2026, 10, 5), LastSunday), (last.WeekStart, last.WeekEnd));
        Assert.Equal([(1, "alice", 120), (2, "bob", 80)], last.Rows.Select(r => (r.Rank, r.Username, r.Points)));
        Assert.Equal(30, (await BoardAsync(alice, "thisWeek")).Rows.Single(r => r.IsYou).Points);   // this week is unchanged
    }

    [Fact] public async Task AcceptedInvites_CountsWhoJoinedThroughYourInvite()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bob");
        var carol = await SignUpAsync("carol");
        var invite = await alice.Client.GetFromJsonAsync<FriendInviteResponse>("/v1/friends/invite", ApiFormat.Json);
        Assert.Equal(HttpStatusCode.OK, (await bob.Client.PostAsync($"/v1/invites/{invite!.Code}/accept", null)).StatusCode);
        await MakeFriendsAsync(alice, carol);   // a plain request doesn't count

        var count = await alice.Client.GetFromJsonAsync<CountResponse>("/v1/me/invites/accepted", ApiFormat.Json);
        Assert.Equal(1, count!.Count);
        Assert.Equal(0, (await bob.Client.GetFromJsonAsync<CountResponse>("/v1/me/invites/accepted", ApiFormat.Json))!.Count);
    }
}
