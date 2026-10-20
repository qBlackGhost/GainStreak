using System.Net;
using System.Net.Http.Json;
using GymApp.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Server.Tests;

public class StatsEndpointTests : ServerTest
{
    /// <summary>The server's date in these tests: a Wednesday, so the week runs Monday 12 to Sunday 18 October.</summary>
    private static readonly DateOnly Wednesday = new(2026, 10, 14);

    public StatsEndpointTests(PostgresFixture postgres) : base(postgres) =>
        Clock.Fixed = new DateTimeOffset(Wednesday.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

    private static Task<HttpResponseMessage> UploadAsync(SignedIn user, params StatsDayRequest[] days) =>
        user.Client.PutAsJsonAsync("/v1/me/stats", new StatsUploadRequest(days), ApiFormat.Json);

    private static async Task UploadOkAsync(SignedIn user, params StatsDayRequest[] days) =>
        Assert.Equal(HttpStatusCode.NoContent, (await UploadAsync(user, days)).StatusCode);

    private static async Task<LeaderboardResponse> BoardAsync(SignedIn user, string period, DateOnly today)
    {
        var response = await user.Client.GetAsync($"/v1/leaderboard?period={period}&today={today:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LeaderboardResponse>(ApiFormat.Json))!;
    }

    private static async Task MakeFriendsAsync(SignedIn a, SignedIn b)
    {
        Assert.Equal(HttpStatusCode.OK, (await a.Client.PostAsync($"/v1/friends/{b.User.Id}/request", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await b.Client.PostAsync($"/v1/friends/{a.User.Id}/accept", null)).StatusCode);
    }

    private static StatsDayRequest Day(DateOnly date, int total, int week, int streak) => new(date, total, week, streak);

    [Fact] public async Task ThreeFriends_RankedLikeTheApp()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bob");
        var carol = await SignUpAsync("carol");
        await MakeFriendsAsync(alice, bob);
        await MakeFriendsAsync(carol, alice);

        await UploadOkAsync(alice, Day(Wednesday, 500, 40, 3));
        await UploadOkAsync(bob, Day(Wednesday, 900, 40, 7));
        await UploadOkAsync(carol, Day(Wednesday, 100, 60, 1));

        var week = await BoardAsync(alice, "thisWeek", Wednesday);
        Assert.Equal(LeaderboardPeriod.ThisWeek, week.Period);
        Assert.Equal((new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 18)), (week.WeekStart, week.WeekEnd));
        Assert.Equal([(1, "carol", 60), (2, "alice", 40), (2, "bob", 40)], week.Rows.Select(r => (r.Rank, r.Username, r.Points)));
        Assert.Equal("alice", Assert.Single(week.Rows, r => r.IsYou).Username);
        Assert.Equal(7, week.Rows.Single(r => r.Username == "bob").CurrentStreak);

        var allTime = await BoardAsync(alice, "allTime", Wednesday);
        Assert.Equal(["bob", "alice", "carol"], allTime.Rows.Select(r => r.Username));
        Assert.Equal(900, allTime.Rows[0].Points);
        var level = GymApp.Core.LevelCalculator.For(900);
        Assert.Equal((level.Number, level.Name), (allTime.Rows[0].Level, allTime.Rows[0].LevelName));
    }

    [Fact] public async Task NonFriendsAndPendingRequests_NeverShow()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bob");
        var stranger = await SignUpAsync("stranger");
        await UploadOkAsync(bob, Day(Wednesday, 900, 90, 5));
        await UploadOkAsync(stranger, Day(Wednesday, 900, 90, 5));
        Assert.Equal(HttpStatusCode.OK, (await alice.Client.PostAsync($"/v1/friends/{bob.User.Id}/request", null)).StatusCode);

        Assert.Equal(["alice"], (await BoardAsync(alice, "allTime", Wednesday)).Rows.Select(r => r.Username));
        Assert.Equal(["bob"], (await BoardAsync(bob, "allTime", Wednesday)).Rows.Select(r => r.Username));
    }

    [Fact] public async Task NoUpload_ShowsZero()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bob");
        await MakeFriendsAsync(alice, bob);
        await UploadOkAsync(alice, Day(Wednesday, 10, 10, 1));

        var bobRow = (await BoardAsync(alice, "allTime", Wednesday)).Rows.Single(r => r.Username == "bob");
        Assert.Equal((0, 0), (bobRow.Points, bobRow.CurrentStreak));
    }

    [Fact] public async Task TheViewersToday_PicksTheProjectedDay()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bob");
        await MakeFriendsAsync(alice, bob);
        // Bob's projection: Thursday is a rest day (streak grows), Friday a missed training day, Saturday the break shows
        await UploadOkAsync(bob,
            Day(Wednesday, 200, 30, 4),
            Day(Wednesday.AddDays(1), 200, 30, 5),
            Day(Wednesday.AddDays(2), 200, 30, 5),
            Day(Wednesday.AddDays(3), 200, 30, 0));

        int BobStreak(LeaderboardResponse b) => b.Rows.Single(r => r.Username == "bob").CurrentStreak;
        Assert.Equal(4, BobStreak(await BoardAsync(alice, "allTime", Wednesday)));
        Assert.Equal(5, BobStreak(await BoardAsync(alice, "allTime", Wednesday.AddDays(1))));
        Assert.Equal(5, BobStreak(await BoardAsync(alice, "allTime", Wednesday.AddDays(2))));

        // A viewer a day behind (another time zone) sees the day before; nothing earlier than the first row shows 0
        Assert.Equal(0, BobStreak(await BoardAsync(alice, "allTime", Wednesday.AddDays(-1))));
    }

    [Fact] public async Task PastTheHorizon_TheLastRowStillCounts()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bob");
        await MakeFriendsAsync(alice, bob);
        await UploadOkAsync(bob, Day(Wednesday.AddDays(-2), 300, 0, 0));

        var row = (await BoardAsync(alice, "allTime", Wednesday)).Rows.Single(r => r.Username == "bob");
        Assert.Equal((300, 0), (row.Points, row.CurrentStreak));
    }

    [Fact] public async Task WeekRollover_WeekPointsResetButTotalsStay()
    {
        var sunday = new DateOnly(2026, 10, 18);
        var monday = sunday.AddDays(1);
        Clock.Fixed = new DateTimeOffset(sunday.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bob");
        await MakeFriendsAsync(alice, bob);
        await UploadOkAsync(bob, Day(sunday, 400, 70, 6));   // bob's phone hasn't uploaded since Sunday

        var sundayBoard = await BoardAsync(alice, "thisWeek", sunday);
        Assert.Equal(70, sundayBoard.Rows.Single(r => r.Username == "bob").Points);

        var mondayWeek = await BoardAsync(alice, "thisWeek", monday);
        Assert.Equal(new DateOnly(2026, 10, 19), mondayWeek.WeekStart);
        Assert.Equal(0, mondayWeek.Rows.Single(r => r.Username == "bob").Points);
        Assert.Equal(400, (await BoardAsync(alice, "allTime", monday)).Rows.Single(r => r.Username == "bob").Points);
    }

    [Fact] public async Task Upload_ReplacesEveryEarlierRow()
    {
        var alice = await SignUpAsync("alice");
        await UploadOkAsync(alice, Day(Wednesday, 10, 10, 1), Day(Wednesday.AddDays(1), 10, 10, 2));
        await UploadOkAsync(alice, Day(Wednesday.AddDays(1), 50, 50, 3));

        await using var db = Db();
        var row = await db.ProjectedStats.SingleAsync();
        Assert.Equal((Wednesday.AddDays(1), 50), (row.Date, row.TotalPoints));
    }

    [Fact] public async Task BadUploads_AreRejected_AndKeepTheOldRows()
    {
        var alice = await SignUpAsync("alice");
        await UploadOkAsync(alice, Day(Wednesday, 10, 10, 1));

        async Task Rejected(string message, params StatsDayRequest[] days) =>
            Assert.Equal(message, await ProblemAsync(await UploadAsync(alice, days), HttpStatusCode.BadRequest));

        await Rejected("Send at least one day.");
        await Rejected("Send at most 31 days.", Enumerable.Range(0, 32).Select(i => Day(Wednesday.AddDays(i), 1, 1, 1)).ToArray());
        await Rejected("Each date can only be sent once.", Day(Wednesday, 1, 1, 1), Day(Wednesday, 2, 2, 2));
        await Rejected("The first day must be within 2 days of today.", Day(Wednesday.AddDays(3), 1, 1, 1));
        await Rejected("The first day must be within 2 days of today.", Day(Wednesday.AddDays(-3), 1, 1, 1));
        await Rejected("Points and streaks can't be negative.", Day(Wednesday, 1, -1, 1));

        await using var db = Db();
        Assert.Equal(10, (await db.ProjectedStats.SingleAsync()).TotalPoints);
    }

    [Fact] public async Task Leaderboard_BadQuery_Is400()
    {
        var alice = await SignUpAsync("alice");

        async Task<string?> Get(string query) =>
            await ProblemAsync(await alice.Client.GetAsync($"/v1/leaderboard?{query}"), HttpStatusCode.BadRequest);

        Assert.Equal("Set period to thisWeek or allTime.", await Get("today=2026-10-14"));
        Assert.Equal("Set period to thisWeek or allTime.", await Get("period=month&today=2026-10-14"));
        Assert.Equal("Send today's date as yyyy-MM-dd.", await Get("period=allTime"));
        Assert.Equal("Send today's date as yyyy-MM-dd.", await Get("period=allTime&today=14.10.2026"));
        Assert.Equal("Today must be within 2 days of the server's date.", await Get("period=allTime&today=2026-10-17"));
    }

    [Fact] public async Task DeletingTheAccount_RemovesItsStats()
    {
        var alice = await SignUpAsync("alice");
        await UploadOkAsync(alice, Day(Wednesday, 10, 10, 1));

        var delete = new HttpRequestMessage(HttpMethod.Delete, "/v1/me") { Content = JsonContent.Create(new DeleteAccountRequest(DefaultPassword)) };
        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.SendAsync(delete)).StatusCode);

        await using var db = Db();
        Assert.Empty(await db.ProjectedStats.ToListAsync());
    }
}
