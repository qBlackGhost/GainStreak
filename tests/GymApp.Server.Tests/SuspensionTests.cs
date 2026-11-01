using System.Net;
using System.Net.Http.Json;
using GymApp.Contracts;
using GymApp.Core;
using GymApp.Server.Endpoints;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Server.Tests;

public class SuspensionTests(PostgresFixture postgres) : ServerTest(postgres)
{
    private async Task RunAsync(params string[] args)
    {
        await using var db = Db();
        var (ok, message) = await AccountCommands.RunAsync(args, db, Clock);
        Assert.True(ok, message);
    }

    private Task<HttpResponseMessage> LoginAsync(string login) =>
        Client.PostAsJsonAsync("/v1/auth/login", new LoginRequest(login, DefaultPassword, "phone"), ApiFormat.Json);

    private static async Task<T> GetAsync<T>(SignedIn user, string url) =>
        (await user.Client.GetFromJsonAsync<T>(url, ApiFormat.Json))!;

    private static async Task MakeFriendsAsync(SignedIn a, SignedIn b)
    {
        await a.Client.PostAsync($"/v1/friends/{b.User.Id}/request", null);
        await b.Client.PostAsync($"/v1/friends/{a.User.Id}/accept", null);
    }

    [Fact] public async Task Suspend_EndsSessions_AndRefusesSignIn_WithTheReason()
    {
        var bob = await SignUpAsync("bobby");

        await RunAsync("suspend", "BOBBY");

        Assert.Equal(HttpStatusCode.Unauthorized, (await bob.Client.GetAsync("/v1/me")).StatusCode);
        var login = await LoginAsync("bobby");
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        Assert.Equal(AccountEndpoints.Suspended, (await login.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail);
    }

    [Fact] public async Task WrongPassword_DoesntTellThatAnAccountIsSuspended()
    {
        await SignUpAsync("bobby");
        await RunAsync("suspend", "bobby");

        var response = await Client.PostAsJsonAsync("/v1/auth/login", new LoginRequest("bobby", "wrong password", "phone"), ApiFormat.Json);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact] public async Task Suspended_DisappearsForOthers_AndUnsuspendPutsItAllBack()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bobby");
        var carol = await SignUpAsync("carol");
        await MakeFriendsAsync(alice, bob);
        await carol.Client.PostAsync($"/v1/friends/{bob.User.Id}/request", null);
        await using (var db = Db())
        {
            db.Shares.Add(new GymApp.Server.Data.Share
            {
                FromUserId = bob.User.Id, ToUserId = alice.User.Id, Kind = GymApp.Server.Data.ShareKind.Workout,
                Payload = "{\"dayName\":\"Push\",\"exercises\":[]}", CreatedAt = DateTime.UtcNow
            });
            db.CheckIns.Add(new GymApp.Server.Data.CheckIn
            {
                UserId = bob.User.Id, Date = DateOnly.FromDateTime(DateTime.UtcNow), GymName = "Gym",
                CityOsmType = OsmElementType.Node, CityOsmId = 1, CityName = "Cluj"
            });
            await db.Users.Where(u => u.Id == bob.User.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.ShareGymCheckIns, true));
            await db.SaveChangesAsync();
        }
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString(ApiFormat.Date);
        var board = $"/v1/leaderboard?period=allTime&today={today}";
        var checkIns = $"/v1/friends/checkins?since={today}";

        await RunAsync("suspend", "bobby");

        Assert.Empty(await GetAsync<List<FriendResponse>>(alice, "/v1/friends"));
        Assert.Empty(await GetAsync<List<FoundUserResponse>>(alice, "/v1/users/search?q=bob"));
        Assert.DoesNotContain((await GetAsync<LeaderboardResponse>(alice, board)).Rows, r => r.Username == "bobby");
        Assert.Empty(await GetAsync<List<FriendCheckInResponse>>(alice, checkIns));
        Assert.Empty(await GetAsync<List<ShareResponse>>(alice, "/v1/shares"));
        Assert.Empty((await GetAsync<FriendRequestsResponse>(carol, "/v1/friends/requests")).Outgoing);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.Client.PostAsync($"/v1/friends/{bob.User.Id}/request", null)).StatusCode);

        await RunAsync("unsuspend", "bobby");

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync("bobby")).StatusCode);
        Assert.Equal(["bobby"], (await GetAsync<List<FriendResponse>>(alice, "/v1/friends")).Select(f => f.Username));
        Assert.Contains((await GetAsync<LeaderboardResponse>(alice, board)).Rows, r => r.Username == "bobby");
        Assert.Single(await GetAsync<List<FriendCheckInResponse>>(alice, checkIns));
        Assert.Single(await GetAsync<List<ShareResponse>>(alice, "/v1/shares"));
    }

    [Fact] public async Task Suspend_TwiceKeepsTheFirstDate_AndAnUnknownNameFails()
    {
        await SignUpAsync("bobby");
        await RunAsync("suspend", "bobby");
        await using var first = Db();
        var at = await first.Users.Where(u => u.Username == "bobby").Select(u => u.SuspendedAt).SingleAsync();

        await RunAsync("suspend", "bobby");

        await using var second = Db();
        Assert.Equal(at, await second.Users.Where(u => u.Username == "bobby").Select(u => u.SuspendedAt).SingleAsync());
        Assert.False((await AccountCommands.RunAsync(["suspend", "nobody"], second, Clock)).Ok);
    }

    [Fact] public async Task TheAppSignsOut_AndShowsWhyAtTheNextLogin()
    {
        var phone = await PhoneAsync("bobby");
        await RunAsync("suspend", "bobby");

        var result = await phone.Accounts.SignInAsync("bobby", DefaultPassword);

        Assert.False(result.Success);
        Assert.Equal(AccountEndpoints.Suspended, result.Error);
    }

    [Fact] public async Task Register_RefusesAFilteredUsername()
    {
        var response = await Client.PostAsJsonAsync("/v1/auth/register",
            new RegisterRequest("big_fuck", "x@example.com", DefaultPassword, "phone"), ApiFormat.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
