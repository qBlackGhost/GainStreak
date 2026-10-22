using System.Net;
using System.Net.Http.Json;
using GymApp.Contracts;

namespace GymApp.Server.Tests;

public class ShareEndpointTests(PostgresFixture postgres) : ServerTest(postgres)
{
    private static readonly SharedWorkout Legs = new("Legs", "PPL", [new("Back squat", "Quads", false, 1, 4)]);
    private static readonly SharedSplit Mine = new("My split", null, null,
        [new("Legs", DayOfWeek.Monday, Legs.Exercises), new("Upper", DayOfWeek.Thursday, [])]);

    private static async Task BefriendAsync(SignedIn a, SignedIn b)
    {
        Assert.Equal(HttpStatusCode.OK, (await a.Client.PostAsync($"/v1/friends/{b.User.Id}/request", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await b.Client.PostAsync($"/v1/friends/{a.User.Id}/accept", null)).StatusCode);
    }

    private static Task<HttpResponseMessage> ShareAsync(SignedIn from, ShareRequest request) =>
        from.Client.PostAsJsonAsync("/v1/shares", request, ApiFormat.Json);

    private static async Task<List<ShareResponse>> IncomingAsync(SignedIn user) =>
        (await user.Client.GetFromJsonAsync<List<ShareResponse>>("/v1/shares", ApiFormat.Json))!;

    [Fact] public async Task Friends_GetTheShare_NewestFirst_AndOnlyTheReceiverRemovesIt()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bob");
        var carol = await SignUpAsync("carol");
        await BefriendAsync(alice, bob);
        await BefriendAsync(alice, carol);

        var sent = await ShareAsync(alice, new ShareRequest([bob.User.Id, carol.User.Id], null, Legs));
        Assert.Equal(2, (await sent.Content.ReadFromJsonAsync<SendShareResponse>(ApiFormat.Json))!.Sent);
        await ShareAsync(alice, new ShareRequest([bob.User.Id], Mine, null));

        var bobs = await IncomingAsync(bob);
        Assert.Equal([true, false], bobs.Select(s => s.Split is not null));
        Assert.Equal(("alice", Mine.Name, DayOfWeek.Thursday), (bobs[0].FromUsername, bobs[0].Split!.Name, bobs[0].Split!.Days[1].Weekday));
        Assert.Equal("Back squat", bobs[1].Workout!.Exercises.Single().Name);
        Assert.Single(await IncomingAsync(carol));
        Assert.Empty(await IncomingAsync(alice));

        Assert.Equal(HttpStatusCode.NotFound, (await carol.Client.DeleteAsync($"/v1/shares/{bobs[0].Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await bob.Client.DeleteAsync($"/v1/shares/{bobs[0].Id}")).StatusCode);
        Assert.Single(await IncomingAsync(bob));
    }

    [Fact] public async Task OnlyFriends_AndValidShares_AreAccepted()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bob");
        var stranger = await SignUpAsync("stranger");
        await BefriendAsync(alice, bob);

        Assert.Equal(ShareRules.NotFriendsMessage, await ProblemAsync(
            await ShareAsync(alice, new ShareRequest([bob.User.Id, stranger.User.Id], null, Legs)), HttpStatusCode.BadRequest));
        Assert.Equal(ShareRules.OneThingMessage, await ProblemAsync(
            await ShareAsync(alice, new ShareRequest([bob.User.Id], Mine, Legs)), HttpStatusCode.BadRequest));
        Assert.Equal(ShareRules.EmptyMessage, await ProblemAsync(
            await ShareAsync(alice, new ShareRequest([bob.User.Id], null, Legs with { Exercises = [] })), HttpStatusCode.BadRequest));
        Assert.Empty(await IncomingAsync(bob));
        Assert.Empty(await IncomingAsync(stranger));
    }

    [Fact] public async Task OldShares_AndSharesFromRemovedFriends_AreNotListed()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bob");
        var carol = await SignUpAsync("carol");
        await BefriendAsync(alice, bob);
        await BefriendAsync(carol, bob);

        Clock.Fixed = DateTimeOffset.UtcNow - ShareRules.KeptFor - TimeSpan.FromDays(1);
        await ShareAsync(alice, new ShareRequest([bob.User.Id], null, Legs));
        Clock.Fixed = null;
        await ShareAsync(carol, new ShareRequest([bob.User.Id], null, Legs));
        Assert.Equal(["carol"], (await IncomingAsync(bob)).Select(s => s.FromUsername));

        Assert.Equal(HttpStatusCode.NoContent, (await bob.Client.DeleteAsync($"/v1/friends/{carol.User.Id}")).StatusCode);
        Assert.Empty(await IncomingAsync(bob));
    }
}
