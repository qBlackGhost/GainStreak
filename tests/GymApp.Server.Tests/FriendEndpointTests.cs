using System.Net;
using System.Net.Http.Json;
using GymApp.Contracts;
using GymApp.Core;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Server.Tests;

public class FriendEndpointTests(PostgresFixture postgres) : ServerTest(postgres)
{
    private static async Task<T> GetAsync<T>(SignedIn user, string url) =>
        (await user.Client.GetFromJsonAsync<T>(url, ApiFormat.Json))!;

    private static async Task<SendRequestResponse> SendAsync(SignedIn from, SignedIn to)
    {
        var response = await from.Client.PostAsync($"/v1/friends/{to.User.Id}/request", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SendRequestResponse>(ApiFormat.Json))!;
    }

    [Fact] public async Task FullFlow_SearchRequestAcceptRemove()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("Bobby");

        var found = Assert.Single(await GetAsync<List<FoundUserResponse>>(alice, "/v1/users/search?q=BOB"));
        Assert.Equal(new FoundUserResponse(bob.User.Id, "Bobby", FriendRelation.None), found);

        Assert.False((await SendAsync(alice, bob)).BecameFriends);
        Assert.Equal(FriendRelation.RequestSent,
            Assert.Single(await GetAsync<List<FoundUserResponse>>(alice, "/v1/users/search?q=bob")).Relation);
        Assert.Equal(1, (await GetAsync<CountResponse>(bob, "/v1/friends/requests/count")).Count);
        var bobsRequests = await GetAsync<FriendRequestsResponse>(bob, "/v1/friends/requests");
        Assert.Equal(["alice"], bobsRequests.Incoming.Select(r => r.Username));
        Assert.Empty(bobsRequests.Outgoing);
        Assert.Equal(["Bobby"], (await GetAsync<FriendRequestsResponse>(alice, "/v1/friends/requests")).Outgoing.Select(r => r.Username));

        Assert.Equal(HttpStatusCode.NoContent, (await bob.Client.PostAsync($"/v1/friends/{alice.User.Id}/accept", null)).StatusCode);
        Assert.Equal(["Bobby"], (await GetAsync<List<FriendResponse>>(alice, "/v1/friends")).Select(f => f.Username));
        Assert.Equal(["alice"], (await GetAsync<List<FriendResponse>>(bob, "/v1/friends")).Select(f => f.Username));
        Assert.Equal(0, (await GetAsync<CountResponse>(bob, "/v1/friends/requests/count")).Count);

        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.DeleteAsync($"/v1/friends/{bob.User.Id}")).StatusCode);
        Assert.Empty(await GetAsync<List<FriendResponse>>(alice, "/v1/friends"));
        Assert.Empty(await GetAsync<List<FriendResponse>>(bob, "/v1/friends"));
    }

    [Fact] public async Task DeclineAndCancel_RemoveTheRequest()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bob");

        await SendAsync(alice, bob);
        Assert.Equal(HttpStatusCode.NoContent, (await bob.Client.PostAsync($"/v1/friends/{alice.User.Id}/decline", null)).StatusCode);
        Assert.Empty((await GetAsync<FriendRequestsResponse>(alice, "/v1/friends/requests")).Outgoing);

        await SendAsync(alice, bob);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.DeleteAsync($"/v1/friends/{bob.User.Id}/request")).StatusCode);
        Assert.Empty((await GetAsync<FriendRequestsResponse>(bob, "/v1/friends/requests")).Incoming);
    }

    [Fact] public async Task SendingBack_AcceptsTheRequest()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bob");

        await SendAsync(alice, bob);
        Assert.True((await SendAsync(bob, alice)).BecameFriends);
        Assert.Single(await GetAsync<List<FriendResponse>>(alice, "/v1/friends"));
    }

    [Fact] public async Task AThirdUser_CantAcceptOrDeclineSomeoneElsesRequest()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bob");
        var carol = await SignUpAsync("carol");
        await SendAsync(alice, bob);

        // Carol only ever acts as herself: there's no request between her and alice
        var accept = await carol.Client.PostAsync($"/v1/friends/{alice.User.Id}/accept", null);
        Assert.Equal("There's no request to accept.", await ProblemAsync(accept, HttpStatusCode.BadRequest));
        var decline = await carol.Client.PostAsync($"/v1/friends/{alice.User.Id}/decline", null);
        Assert.Equal("There's no request to decline.", await ProblemAsync(decline, HttpStatusCode.BadRequest));
        // ...and the sender can't accept their own
        var own = await alice.Client.PostAsync($"/v1/friends/{bob.User.Id}/accept", null);
        Assert.Equal("You can't accept your own request.", await ProblemAsync(own, HttpStatusCode.BadRequest));

        await using var db = Db();
        Assert.Equal(FriendshipStatus.Pending, (await db.Friendships.SingleAsync()).Status);
    }

    [Fact] public async Task Refusals_MapToStatusCodes()
    {
        var alice = await SignUpAsync("alice");

        var unknown = await alice.Client.PostAsync("/v1/friends/99999/request", null);
        Assert.Equal("That user doesn't exist.", await ProblemAsync(unknown, HttpStatusCode.NotFound));
        var self = await alice.Client.PostAsync($"/v1/friends/{alice.User.Id}/request", null);
        Assert.Equal("You can't add yourself.", await ProblemAsync(self, HttpStatusCode.BadRequest));
        var shortSearch = await alice.Client.GetAsync("/v1/users/search?q=al");
        Assert.Equal("Type at least 3 characters.", await ProblemAsync(shortSearch, HttpStatusCode.BadRequest));
        var notFriends = await alice.Client.DeleteAsync("/v1/friends/99999");
        Assert.Equal("You're not friends.", await ProblemAsync(notFriends, HttpStatusCode.BadRequest));
    }

    [Fact] public async Task FriendEndpoints_NeedASignIn()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/v1/friends")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/v1/users/search?q=alice")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsync("/v1/friends/1/request", null)).StatusCode);
    }

    [Fact] public async Task Enums_AreSentAsCamelCaseStrings()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bob");
        await SendAsync(bob, alice);

        var json = await alice.Client.GetStringAsync("/v1/users/search?q=bob");
        Assert.Contains("\"relation\":\"requestReceived\"", json);
    }

    [Fact] public async Task BothSendingAtTheSameMoment_NeverMakesTwoRows()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bob");

        for (var round = 0; round < 10; round++)
        {
            var responses = await Task.WhenAll(
                alice.Client.PostAsync($"/v1/friends/{bob.User.Id}/request", null),
                bob.Client.PostAsync($"/v1/friends/{alice.User.Id}/request", null));

            // One wins; the other either saw the request and accepted it, or lost the race and got a conflict
            var outcomes = new List<string>();
            foreach (var r in responses)
            {
                if (r.StatusCode == HttpStatusCode.Conflict)
                {
                    Assert.Equal("That request just changed. Refresh and try again.", await ProblemAsync(r, HttpStatusCode.Conflict));
                    outcomes.Add("conflict");
                }
                else
                {
                    Assert.Equal(HttpStatusCode.OK, r.StatusCode);
                    outcomes.Add((await r.Content.ReadFromJsonAsync<SendRequestResponse>(ApiFormat.Json))!.BecameFriends ? "friends" : "sent");
                }
            }
            outcomes.Sort();
            Assert.True(outcomes is ["conflict", "sent"] or ["friends", "sent"], string.Join(", ", outcomes));

            await using var db = Db();
            var row = await db.Friendships.SingleAsync();
            Assert.Equal(outcomes.Contains("friends") ? FriendshipStatus.Accepted : FriendshipStatus.Pending, row.Status);
            await db.Friendships.ExecuteDeleteAsync();
        }
    }
}
