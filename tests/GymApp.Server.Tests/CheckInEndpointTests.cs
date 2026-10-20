using System.Net;
using System.Net.Http.Json;
using GymApp.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Server.Tests;

public class CheckInEndpointTests : ServerTest
{
    private static readonly DateOnly Today = new(2026, 10, 14);

    public CheckInEndpointTests(PostgresFixture postgres) : base(postgres) =>
        Clock.Fixed = new DateTimeOffset(Today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

    private static CheckInRequest OsmGym(string name = "World Class", long id = 101) =>
        new(OsmType.Way, id, name, OsmType.Relation, 7, "Timișoara", 45.75, 21.23);

    private static CheckInRequest CustomGym(string name = "Garage gym") =>
        new(null, null, name, OsmType.Relation, 7, "Timișoara", 45.70, 21.20);

    private static Task<HttpResponseMessage> PutAsync(SignedIn user, DateOnly date, CheckInRequest gym) =>
        user.Client.PutAsJsonAsync($"/v1/me/checkins/{date:yyyy-MM-dd}", gym, ApiFormat.Json);

    private static async Task PutOkAsync(SignedIn user, DateOnly date, CheckInRequest gym) =>
        Assert.Equal(HttpStatusCode.NoContent, (await PutAsync(user, date, gym)).StatusCode);

    private static async Task ShareAsync(SignedIn user, bool share) =>
        Assert.Equal(HttpStatusCode.NoContent,
            (await user.Client.PutAsJsonAsync("/v1/me/sharing", new SharingRequest(share))).StatusCode);

    private static async Task<List<FriendCheckInResponse>> FriendsCheckInsAsync(SignedIn user, DateOnly since) =>
        (await user.Client.GetFromJsonAsync<List<FriendCheckInResponse>>($"/v1/friends/checkins?since={since:yyyy-MM-dd}", ApiFormat.Json))!;

    private static async Task MakeFriendsAsync(SignedIn a, SignedIn b)
    {
        Assert.Equal(HttpStatusCode.OK, (await a.Client.PostAsync($"/v1/friends/{b.User.Id}/request", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await b.Client.PostAsync($"/v1/friends/{a.User.Id}/accept", null)).StatusCode);
    }

    private async Task<(SignedIn Alice, SignedIn Bob)> FriendsAsync()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bob");
        await MakeFriendsAsync(alice, bob);
        return (alice, bob);
    }

    [Fact] public async Task Shared_FriendSeesGymAndDay()
    {
        var (alice, bob) = await FriendsAsync();
        await ShareAsync(bob, true);
        await PutOkAsync(bob, Today, OsmGym());

        var c = Assert.Single(await FriendsCheckInsAsync(alice, Today.AddDays(-7)));
        Assert.Equal(new FriendCheckInResponse(bob.User.Id, "bob", Today, OsmType.Way, 101, "World Class",
            OsmType.Relation, 7, "Timișoara", 45.75, 21.23), c);
        Assert.True((await alice.Client.GetFromJsonAsync<MeResponse>("/v1/me"))!.ShareGymCheckIns is null);
        Assert.True((await bob.Client.GetFromJsonAsync<MeResponse>("/v1/me"))!.ShareGymCheckIns);
    }

    [Theory]
    [InlineData(null)]    // never asked
    [InlineData(false)]
    public async Task NotSharing_PutIs403_AndNothingIsStored(bool? share)
    {
        var (_, bob) = await FriendsAsync();
        if (share is { } s) await ShareAsync(bob, s);

        var response = await PutAsync(bob, Today, OsmGym());
        Assert.Equal("Turn on sharing gym check-ins first.", await ProblemAsync(response, HttpStatusCode.Forbidden));
        await using var db = Db();
        Assert.Empty(await db.CheckIns.ToListAsync());
    }

    [Fact] public async Task TurningSharingOff_DeletesEveryCheckIn()
    {
        var (alice, bob) = await FriendsAsync();
        await ShareAsync(bob, true);
        await PutOkAsync(bob, Today, OsmGym());
        await PutOkAsync(bob, Today.AddDays(-1), OsmGym());

        await ShareAsync(bob, false);

        Assert.Empty(await FriendsCheckInsAsync(alice, Today.AddDays(-7)));
        await using var db = Db();
        Assert.Empty(await db.CheckIns.ToListAsync());
    }

    [Fact] public async Task CustomGym_CoordinatesAreDropped()
    {
        var (alice, bob) = await FriendsAsync();
        await ShareAsync(bob, true);
        await PutOkAsync(bob, Today, CustomGym());

        await using (var db = Db())
        {
            var row = await db.CheckIns.SingleAsync();
            Assert.Equal((null, null), (row.Latitude, row.Longitude));
        }
        var c = Assert.Single(await FriendsCheckInsAsync(alice, Today));
        Assert.Equal(("Garage gym", null, null, null), (c.GymName, c.GymOsmId, c.Latitude, c.Longitude));
    }

    [Fact] public async Task NonFriendsAndPendingFriends_SeeNothing()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bob");
        var stranger = await SignUpAsync("stranger");
        await ShareAsync(alice, true);
        await PutOkAsync(alice, Today, OsmGym());
        Assert.Equal(HttpStatusCode.OK, (await alice.Client.PostAsync($"/v1/friends/{bob.User.Id}/request", null)).StatusCode);

        Assert.Empty(await FriendsCheckInsAsync(bob, Today.AddDays(-7)));
        Assert.Empty(await FriendsCheckInsAsync(stranger, Today.AddDays(-7)));
        Assert.Empty(await FriendsCheckInsAsync(alice, Today.AddDays(-7)));   // not your own either
    }

    [Fact] public async Task ThirtyDayWindow_Holds_WhateverSinceSays()
    {
        var (alice, bob) = await FriendsAsync();
        await ShareAsync(bob, true);
        await PutOkAsync(bob, Today.AddDays(-29), OsmGym("Inside"));
        await PutOkAsync(bob, Today.AddDays(-30), OsmGym("Outside"));
        await PutOkAsync(bob, Today.AddDays(-3), OsmGym("Recent"));

        Assert.Equal(["Recent", "Inside"], (await FriendsCheckInsAsync(alice, Today.AddDays(-365))).Select(c => c.GymName));
        Assert.Equal(["Recent"], (await FriendsCheckInsAsync(alice, Today.AddDays(-3))).Select(c => c.GymName));
    }

    [Fact] public async Task Undo_DeletesTheRow()
    {
        var (alice, bob) = await FriendsAsync();
        await ShareAsync(bob, true);
        await PutOkAsync(bob, Today, OsmGym());

        Assert.Equal(HttpStatusCode.NoContent, (await bob.Client.DeleteAsync($"/v1/me/checkins/{Today:yyyy-MM-dd}")).StatusCode);
        Assert.Empty(await FriendsCheckInsAsync(alice, Today));
        // Undoing again is fine
        Assert.Equal(HttpStatusCode.NoContent, (await bob.Client.DeleteAsync($"/v1/me/checkins/{Today:yyyy-MM-dd}")).StatusCode);
    }

    [Fact] public async Task SameDayAgain_ReplacesTheGym()
    {
        var (alice, bob) = await FriendsAsync();
        await ShareAsync(bob, true);
        await PutOkAsync(bob, Today, OsmGym("First"));
        await PutOkAsync(bob, Today, OsmGym("Second", 202));

        var c = Assert.Single(await FriendsCheckInsAsync(alice, Today));
        Assert.Equal(("Second", 202L), (c.GymName, c.GymOsmId));
    }

    [Fact] public async Task FutureDates_BeyondTheAllowance_AreRefused()
    {
        var (_, bob) = await FriendsAsync();
        await ShareAsync(bob, true);

        await PutOkAsync(bob, Today.AddDays(2), OsmGym());
        var response = await PutAsync(bob, Today.AddDays(3), OsmGym());
        Assert.Equal("A check-in can't be in the future.", await ProblemAsync(response, HttpStatusCode.BadRequest));
    }

    [Fact] public async Task BadRequests_Are400()
    {
        var (alice, bob) = await FriendsAsync();
        await ShareAsync(bob, true);

        async Task<string?> Put(string date, CheckInRequest gym) =>
            await ProblemAsync(await bob.Client.PutAsJsonAsync($"/v1/me/checkins/{date}", gym, ApiFormat.Json), HttpStatusCode.BadRequest);

        Assert.Equal("Send the date as yyyy-MM-dd.", await Put("14-10-2026", OsmGym()));
        Assert.Equal("Send the gym's name.", await Put("2026-10-14", OsmGym(" ")));
        Assert.Equal("Send both the gym's OSM type and id, or neither for a custom gym.",
            await Put("2026-10-14", OsmGym() with { GymOsmId = null }));
        Assert.Equal("Send both coordinates, or neither.", await Put("2026-10-14", OsmGym() with { Longitude = null }));
        Assert.Equal("Those coordinates aren't on the map.", await Put("2026-10-14", OsmGym() with { Latitude = 91 }));
        Assert.Equal("Send since as yyyy-MM-dd.",
            await ProblemAsync(await alice.Client.GetAsync("/v1/friends/checkins"), HttpStatusCode.BadRequest));
    }

    [Fact] public async Task Cleanup_DeletesCheckInsOlderThan31Days()
    {
        var (_, bob) = await FriendsAsync();
        await ShareAsync(bob, true);
        await PutOkAsync(bob, Today.AddDays(-31), OsmGym("Kept"));
        await PutOkAsync(bob, Today.AddDays(-32), OsmGym("Deleted"));

        await using var db = Db();
        Assert.Equal(1, await CheckInCleanup.RunAsync(db, Clock));
        Assert.Equal("Kept", (await db.CheckIns.SingleAsync()).GymName);
    }

    [Fact] public async Task DeletingTheAccount_RemovesItsCheckIns()
    {
        var (_, bob) = await FriendsAsync();
        await ShareAsync(bob, true);
        await PutOkAsync(bob, Today, OsmGym());

        var delete = new HttpRequestMessage(HttpMethod.Delete, "/v1/me") { Content = JsonContent.Create(new DeleteAccountRequest(DefaultPassword)) };
        Assert.Equal(HttpStatusCode.NoContent, (await bob.Client.SendAsync(delete)).StatusCode);

        await using var db = Db();
        Assert.Empty(await db.CheckIns.ToListAsync());
    }
}
