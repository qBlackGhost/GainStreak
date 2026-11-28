using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GymApp.Contracts;
using GymApp.Core;
using GymApp.Server.Endpoints;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Server.Tests;

/// <summary><c>PUT /v1/me/username</c> (Phase 28).</summary>
public class UsernameChangeTests : ServerTest
{
    /// <summary>A Wednesday, as in the stats and check-in tests.</summary>
    private static readonly DateOnly Today = new(2026, 10, 14);
    private static readonly DateTimeOffset Noon = new(Today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

    public UsernameChangeTests(PostgresFixture postgres) : base(postgres) => Clock.Fixed = Noon;

    private static Task<HttpResponseMessage> RenameAsync(SignedIn user, string username, string password = DefaultPassword) =>
        user.Client.PutAsJsonAsync("/v1/me/username", new ChangeUsernameRequest(username, password), ApiFormat.Json);

    private static async Task<MeResponse> RenameOkAsync(SignedIn user, string username)
    {
        var response = await RenameAsync(user, username);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MeResponse>(ApiFormat.Json))!;
    }

    private static async Task<(string? Detail, string? Field)> ProblemWithFieldAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>())!;
        return (problem.Detail, problem.Extensions.TryGetValue("field", out var f) ? f?.ToString() : null);
    }

    private static async Task BefriendAsync(SignedIn a, SignedIn b)
    {
        Assert.Equal(HttpStatusCode.OK, (await a.Client.PostAsync($"/v1/friends/{b.User.Id}/request", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await b.Client.PostAsync($"/v1/friends/{a.User.Id}/accept", null)).StatusCode);
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string login) =>
        client.PostAsJsonAsync("/v1/auth/login", new LoginRequest(login, DefaultPassword, "tablet"), ApiFormat.Json);

    [Fact] public async Task Rename_ShowsEverywhereForAFriend_AtOnce()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bobby");
        await BefriendAsync(alice, bob);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.PutAsJsonAsync("/v1/me/stats",
            new StatsUploadRequest([new StatsDayRequest(Today, 100, 40, 3)]), ApiFormat.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.PutAsJsonAsync("/v1/me/sharing", new SharingRequest(true))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.PutAsJsonAsync($"/v1/me/checkins/{Today:yyyy-MM-dd}",
            new CheckInRequest(OsmType.Way, 101, "World Class", OsmType.Relation, 7, "Timișoara", 45.75, 21.23), ApiFormat.Json)).StatusCode);
        var legs = new SharedWorkout("Legs", "PPL", [new("Back squat", "Quads", false, 1, 4)]);
        Assert.Equal(HttpStatusCode.OK, (await alice.Client.PostAsJsonAsync("/v1/shares",
            new ShareRequest([bob.User.Id], null, legs), ApiFormat.Json)).StatusCode);

        var me = await RenameOkAsync(alice, "Alice_Lifts");
        Assert.Equal(("Alice_Lifts", alice.User.Id), (me.Username, me.Id));

        var friends = await bob.Client.GetFromJsonAsync<List<FriendResponse>>("/v1/friends", ApiFormat.Json);
        Assert.Equal(["Alice_Lifts"], friends!.Select(f => f.Username));
        var shares = await bob.Client.GetFromJsonAsync<List<ShareResponse>>("/v1/shares", ApiFormat.Json);
        Assert.Equal(["Alice_Lifts"], shares!.Select(s => s.FromUsername));
        var board = await bob.Client.GetFromJsonAsync<LeaderboardResponse>($"/v1/leaderboard?period=thisWeek&today={Today:yyyy-MM-dd}", ApiFormat.Json);
        Assert.Equal("Alice_Lifts", board!.Rows.Single(r => r.UserId == alice.User.Id).Username);
        var checkIns = await bob.Client.GetFromJsonAsync<List<FriendCheckInResponse>>($"/v1/friends/checkins?since={Today:yyyy-MM-dd}", ApiFormat.Json);
        Assert.Equal(["Alice_Lifts"], checkIns!.Select(c => c.Username));
        Assert.Equal("Alice_Lifts", (await alice.Client.GetFromJsonAsync<MeResponse>("/v1/me", ApiFormat.Json))!.Username);
    }

    [Fact] public async Task Rename_TheNewNameSignsIn_AndTheOldOneIsFreeForAnyone()
    {
        var alice = await SignUpAsync("alice");
        await RenameOkAsync(alice, "alice2");

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(Client, "alice")).StatusCode);
        Assert.Equal(alice.User.Id, (await LogInAsync("ALICE2")).User.Id);
        // Decision 5: the old name isn't held
        var taken = await Client.PostAsJsonAsync("/v1/auth/register",
            new RegisterRequest("alice", "someone.else@example.com", DefaultPassword, "phone"), ApiFormat.Json);
        Assert.Equal(HttpStatusCode.Created, taken.StatusCode);
    }

    [Fact] public async Task Rename_KeepsTheInviteCodeAndLink_AndTheLookupShowsTheNewName()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bobby");
        var before = await alice.Client.GetFromJsonAsync<FriendInviteResponse>("/v1/friends/invite", ApiFormat.Json);

        await RenameOkAsync(alice, "alice_new");

        Assert.Equal(before, await alice.Client.GetFromJsonAsync<FriendInviteResponse>("/v1/friends/invite", ApiFormat.Json));
        var lookup = await bob.Client.GetFromJsonAsync<InviteLookupResponse>($"/v1/invites/{before!.Code}", ApiFormat.Json);
        Assert.Equal("alice_new", lookup!.Username);
    }

    [Fact] public async Task TakenName_Is409_InAnyCase()
    {
        var alice = await SignUpAsync("alice");
        await SignUpAsync("bobby");

        Assert.Equal(("That username is taken.", ProblemFields.Username),
            await ProblemWithFieldAsync(await RenameAsync(alice, "BOBBY"), HttpStatusCode.Conflict));
    }

    [Fact] public async Task BadOrFilteredName_Is400_UnderUsername()
    {
        var alice = await SignUpAsync("alice");

        Assert.Equal(("Use only letters, digits, '_', '.' and '-' in your username.", ProblemFields.Username),
            await ProblemWithFieldAsync(await RenameAsync(alice, "al ice"), HttpStatusCode.BadRequest));
        Assert.Equal((Credentials.NeedsALetterMessage, ProblemFields.Username),
            await ProblemWithFieldAsync(await RenameAsync(alice, "12345"), HttpStatusCode.BadRequest));
        Assert.Equal(("That username isn't allowed. Pick another one.", ProblemFields.Username),
            await ProblemWithFieldAsync(await RenameAsync(alice, "gainstreak_admin"), HttpStatusCode.BadRequest));
        Assert.Equal(("That is already your username.", ProblemFields.Username),
            await ProblemWithFieldAsync(await RenameAsync(alice, " alice "), HttpStatusCode.BadRequest));
    }

    [Fact] public async Task WrongPassword_Is400_UnderPassword_AndKeepsTheName()
    {
        var alice = await SignUpAsync("alice");

        Assert.Equal(("That password is not right.", ProblemFields.Password),
            await ProblemWithFieldAsync(await RenameAsync(alice, "alice2", "wrong password"), HttpStatusCode.BadRequest));
        Assert.Equal("alice", (await alice.Client.GetFromJsonAsync<MeResponse>("/v1/me", ApiFormat.Json))!.Username);
    }

    [Fact] public async Task SecondRename_Waits30Days_WithTheDate()
    {
        var alice = await SignUpAsync("alice");
        var first = await RenameOkAsync(alice, "alice2");
        var allowedAt = Noon.UtcDateTime + Credentials.UsernameChangeCooldown;
        Assert.Equal(allowedAt, first.UsernameChangeAllowedAt);
        Assert.Equal(allowedAt, (await alice.Client.GetFromJsonAsync<MeResponse>("/v1/me", ApiFormat.Json))!.UsernameChangeAllowedAt);

        Clock.Fixed = Noon.AddDays(29);
        var tooSoon = await RenameAsync(alice, "alice3");
        Assert.Equal(HttpStatusCode.TooManyRequests, tooSoon.StatusCode);
        var problem = (await tooSoon.Content.ReadFromJsonAsync<ProblemDetails>())!;
        Assert.Equal("You can change your username again on 13 November 2026.", problem.Detail);
        Assert.Equal(allowedAt, ((JsonElement)problem.Extensions["usernameChangeAllowedAt"]!).GetDateTime().ToUniversalTime());

        Clock.Fixed = Noon.AddDays(30);
        Assert.Null((await alice.Client.GetFromJsonAsync<MeResponse>("/v1/me", ApiFormat.Json))!.UsernameChangeAllowedAt);
        Assert.Equal("alice3", (await RenameOkAsync(alice, "alice3")).Username);
    }

    [Fact] public async Task CapitalsOnly_IsAllowedTwiceInARow_AndDoesntStartTheWait()
    {
        var alice = await SignUpAsync("alice");

        Assert.Null((await RenameOkAsync(alice, "Alice")).UsernameChangeAllowedAt);
        Assert.Equal("ALICE", (await RenameOkAsync(alice, "ALICE")).Username);
        // A real rename still works straight after
        Assert.Equal("alice_two", (await RenameOkAsync(alice, "alice_two")).Username);
        // ... and a capitals-only change works inside the wait it started
        Assert.Equal("Alice_Two", (await RenameOkAsync(alice, "Alice_Two")).Username);
    }

    [Fact] public async Task TwoRenamesToTheSameName_AtOnce_OneGets409()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bobby");

        var both = await Task.WhenAll(RenameAsync(alice, "champion"), RenameAsync(bob, "Champion"));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], both.Select(r => r.StatusCode).Order());
        await using var db = Db();
        Assert.Single(await db.Users.Where(u => u.UsernameNormalized == "champion").ToListAsync());
    }

    [Fact] public async Task SuspendedUser_CantRename()
    {
        var alice = await SignUpAsync("alice");
        await using (var db = Db())
        {
            var (ok, message) = await AccountCommands.RunAsync(["suspend", "alice"], db, Clock);
            Assert.True(ok, message);
        }

        // Suspending ends the sessions, so the rename never reaches the endpoint
        Assert.Equal(HttpStatusCode.Unauthorized, (await RenameAsync(alice, "alice2")).StatusCode);
        await using (var db = Db())
            Assert.Equal("alice", (await db.Users.SingleAsync()).Username);
    }

    [Fact] public async Task History_IsKept_AndDeletedWithTheAccount()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bobby");
        await RenameOkAsync(alice, "Alice");
        await RenameOkAsync(alice, "alice2");
        await RenameOkAsync(bob, "bob2");

        await using (var db = Db())
        {
            var history = await db.UsernameChanges.Where(c => c.UserId == alice.User.Id).OrderBy(c => c.Id)
                .Select(c => new { c.OldUsername, c.NewUsername }).ToListAsync();
            Assert.Equal([("alice", "Alice"), ("Alice", "alice2")], history.Select(h => (h.OldUsername, h.NewUsername)));
        }

        var delete = new HttpRequestMessage(HttpMethod.Delete, "/v1/me") { Content = JsonContent.Create(new DeleteAccountRequest(DefaultPassword)) };
        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.SendAsync(delete)).StatusCode);

        await using (var db = Db())
            Assert.Equal([bob.User.Id], await db.UsernameChanges.Select(c => c.UserId).ToListAsync());
    }
}
