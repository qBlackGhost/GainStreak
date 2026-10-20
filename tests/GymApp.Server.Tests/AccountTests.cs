using System.Net;
using System.Net.Http.Json;
using GymApp.Contracts;
using GymApp.Server.Auth;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Server.Tests;

public class AccountTests(PostgresFixture postgres) : ServerTest(postgres)
{
    // --- Register ---

    [Fact] public async Task Register_ThenMe_ReturnsTheAccount()
    {
        var alice = await SignUpAsync("Alice");

        var me = await alice.Client.GetFromJsonAsync<MeResponse>("/v1/me");
        Assert.Equal(alice.User, me);
        Assert.Equal("Alice", me!.Username);
        Assert.Equal("alice@example.com", me.Email);
        Assert.Null(me.ShareGymCheckIns);
    }

    [Fact] public async Task Register_SameUsernameInOtherCase_Is409()
    {
        await SignUpAsync("Alice");

        var response = await Client.PostAsJsonAsync("/v1/auth/register", new RegisterRequest("aLiCe", "other@example.com", DefaultPassword, "x"));
        Assert.Equal("That username is taken.", await ProblemAsync(response, HttpStatusCode.Conflict));
    }

    [Fact] public async Task Register_SameEmailInOtherCase_Is409()
    {
        await SignUpAsync("alice");

        var response = await Client.PostAsJsonAsync("/v1/auth/register", new RegisterRequest("bob", " ALICE@example.com", DefaultPassword, "x"));
        Assert.Equal("That email is already used.", await ProblemAsync(response, HttpStatusCode.Conflict));
    }

    [Fact] public async Task Register_BadInput_Is400WithTheRuleMessage()
    {
        var badEmail = await Client.PostAsJsonAsync("/v1/auth/register", new RegisterRequest("alice", "not-an-email", DefaultPassword, "x"));
        Assert.Equal("Enter a valid email address, e.g. name@example.com.", await ProblemAsync(badEmail, HttpStatusCode.BadRequest));

        var shortPassword = await Client.PostAsJsonAsync("/v1/auth/register", new RegisterRequest("alice", "a@example.com", "short", "x"));
        Assert.Equal("Password must be at least 8 characters.", await ProblemAsync(shortPassword, HttpStatusCode.BadRequest));

        var badName = await Client.PostAsJsonAsync("/v1/auth/register", new RegisterRequest("al ice", "a@example.com", DefaultPassword, "x"));
        Assert.Equal("Use only letters, digits, '_', '.' and '-' in your username.", await ProblemAsync(badName, HttpStatusCode.BadRequest));

        await using var db = Db();
        Assert.Equal(0, await db.Users.CountAsync());
    }

    [Fact] public async Task Register_LongDeviceName_IsCut()
    {
        await SignUpAsync("alice", device: new string('d', 100));

        await using var db = Db();
        Assert.Equal(64, (await db.Sessions.SingleAsync()).DeviceName.Length);
    }

    // --- Login ---

    [Fact] public async Task Login_ByUsernameInAnyCase_And_ByEmail()
    {
        var alice = await SignUpAsync("Alice");

        var byName = await LogInAsync("ALICE");
        var byEmail = await LogInAsync("Alice@Example.com");

        Assert.Equal(alice.User.Id, byName.User.Id);
        Assert.Equal(alice.User.Id, byEmail.User.Id);
        Assert.NotEqual(byName.Token, byEmail.Token);
        Assert.Equal(alice.User.Id, (await byEmail.Client.GetFromJsonAsync<MeResponse>("/v1/me"))!.Id);
    }

    [Fact] public async Task Login_WrongPasswordAndUnknownUser_GiveTheSameMessage()
    {
        await SignUpAsync("alice");

        var wrongPassword = await Client.PostAsJsonAsync("/v1/auth/login", new LoginRequest("alice", "wrong password", "x"));
        var unknownUser = await Client.PostAsJsonAsync("/v1/auth/login", new LoginRequest("nobody", DefaultPassword, "x"));

        Assert.Equal("Wrong username or password.", await ProblemAsync(wrongPassword, HttpStatusCode.Unauthorized));
        Assert.Equal("Wrong username or password.", await ProblemAsync(unknownUser, HttpStatusCode.Unauthorized));
    }

    // --- Tokens ---

    [Fact] public async Task NoOrUnknownToken_Is401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientWith("not-a-token").GetAsync("/v1/me")).StatusCode);
    }

    [Fact] public async Task ExpiredToken_Is401()
    {
        var alice = await SignUpAsync("alice");
        await using (var db = Db())
            await db.Sessions.ExecuteUpdateAsync(u => u.SetProperty(s => s.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));

        Assert.Equal(HttpStatusCode.Unauthorized, (await alice.Client.GetAsync("/v1/me")).StatusCode);
    }

    [Fact] public async Task UsingASession_SlidesItsExpiry_AtMostHourly()
    {
        var alice = await SignUpAsync("alice");
        var lastUsed = DateTime.UtcNow.AddDays(-10);
        await using (var db = Db())
            await db.Sessions.ExecuteUpdateAsync(u => u
                .SetProperty(s => s.LastUsedAt, lastUsed)
                .SetProperty(s => s.ExpiresAt, lastUsed + SessionTokens.Lifetime));

        await alice.Client.GetAsync("/v1/me");

        await using (var db = Db())
        {
            var session = await db.Sessions.SingleAsync();
            Assert.True(session.LastUsedAt > DateTime.UtcNow.AddMinutes(-1));
            Assert.Equal(session.LastUsedAt + SessionTokens.Lifetime, session.ExpiresAt);
        }

        // A second request within the hour doesn't write again
        var slid = (await Db().Sessions.SingleAsync()).LastUsedAt;
        await alice.Client.GetAsync("/v1/me");
        Assert.Equal(slid, (await Db().Sessions.SingleAsync()).LastUsedAt);
    }

    [Fact] public async Task Logout_RevokesOnlyThisSession()
    {
        var phone = await SignUpAsync("alice");
        var tablet = await LogInAsync("alice");

        Assert.Equal(HttpStatusCode.NoContent, (await phone.Client.PostAsync("/v1/auth/logout", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.Client.GetAsync("/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await tablet.Client.GetAsync("/v1/me")).StatusCode);
    }

    // --- Password ---

    [Fact] public async Task ChangePassword_KeepsThisSession_AndKillsTheOthers()
    {
        var phone = await SignUpAsync("alice");
        var tablet = await LogInAsync("alice");

        var response = await phone.Client.PutAsJsonAsync("/v1/me/password", new ChangePasswordRequest(DefaultPassword, "brand new pass"));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await phone.Client.GetAsync("/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await tablet.Client.GetAsync("/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Client.PostAsJsonAsync("/v1/auth/login", new LoginRequest("alice", DefaultPassword, "x"))).StatusCode);
        await LogInAsync("alice", "brand new pass");
    }

    [Fact] public async Task ChangePassword_WrongCurrentOrBadNew_Is400()
    {
        var alice = await SignUpAsync("alice");

        var wrongCurrent = await alice.Client.PutAsJsonAsync("/v1/me/password", new ChangePasswordRequest("wrong password", "brand new pass"));
        Assert.Equal("That password is not right.", await ProblemAsync(wrongCurrent, HttpStatusCode.BadRequest));

        var tooShort = await alice.Client.PutAsJsonAsync("/v1/me/password", new ChangePasswordRequest(DefaultPassword, "short"));
        Assert.Equal("Password must be at least 8 characters.", await ProblemAsync(tooShort, HttpStatusCode.BadRequest));

        await LogInAsync("alice");
    }

    // --- Email ---

    private static Task<HttpResponseMessage> ChangeEmailAsync(SignedIn user, string email, string password = DefaultPassword) =>
        user.Client.PutAsJsonAsync("/v1/me/email", new ChangeEmailRequest(password, email));

    [Fact] public async Task ChangeEmail_OldEmailStopsWorking_AndTheNewOneLogsIn()
    {
        var alice = await SignUpAsync("alice");

        Assert.Equal(HttpStatusCode.NoContent, (await ChangeEmailAsync(alice, " Alice.New@Example.com ")).StatusCode);

        Assert.Equal("alice.new@example.com", (await alice.Client.GetFromJsonAsync<MeResponse>("/v1/me"))!.Email);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Client.PostAsJsonAsync("/v1/auth/login", new LoginRequest("alice@example.com", DefaultPassword, "x"))).StatusCode);
        Assert.Equal(alice.User.Id, (await LogInAsync("alice.new@example.com")).User.Id);
    }

    [Fact] public async Task ChangeEmail_WrongPassword_Is400_AndKeepsTheEmail()
    {
        var alice = await SignUpAsync("alice");

        var response = await ChangeEmailAsync(alice, "new@example.com", "wrong password");
        Assert.Equal("That password is not right.", await ProblemAsync(response, HttpStatusCode.BadRequest));
        Assert.Equal("alice@example.com", (await alice.Client.GetFromJsonAsync<MeResponse>("/v1/me"))!.Email);
    }

    [Fact] public async Task ChangeEmail_TakenInAnotherCase_Is409()
    {
        var alice = await SignUpAsync("alice");
        await SignUpAsync("bob");

        var response = await ChangeEmailAsync(alice, "BOB@example.com");
        Assert.Equal("That email is already used.", await ProblemAsync(response, HttpStatusCode.Conflict));
    }

    [Fact] public async Task ChangeEmail_SameOrBadEmail_Is400()
    {
        var alice = await SignUpAsync("alice");

        var same = await ChangeEmailAsync(alice, "ALICE@example.com");
        Assert.Equal("That is already your email.", await ProblemAsync(same, HttpStatusCode.BadRequest));

        var bad = await ChangeEmailAsync(alice, "not-an-email");
        Assert.Equal("Enter a valid email address, e.g. name@example.com.", await ProblemAsync(bad, HttpStatusCode.BadRequest));
    }

    // --- Delete ---

    [Fact] public async Task DeleteAccount_WrongPassword_Is400_AndKeepsTheAccount()
    {
        var alice = await SignUpAsync("alice");

        var response = await alice.Client.SendAsync(DeleteMe("wrong password"));
        Assert.Equal("That password is not right.", await ProblemAsync(response, HttpStatusCode.BadRequest));
        Assert.Equal(HttpStatusCode.OK, (await alice.Client.GetAsync("/v1/me")).StatusCode);
    }

    [Fact] public async Task DeleteAccount_RemovesEverything_AndTheTokenStopsWorking()
    {
        var phone = await SignUpAsync("alice");
        var tablet = await LogInAsync("alice");
        var bob = await SignUpAsync("bob");
        Assert.Equal(HttpStatusCode.OK, (await bob.Client.PostAsync($"/v1/friends/{phone.User.Id}/request", null)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await phone.Client.SendAsync(DeleteMe(DefaultPassword))).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.Client.GetAsync("/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await tablet.Client.GetAsync("/v1/me")).StatusCode);
        await using (var db = Db())
        {
            Assert.Equal([bob.User.Id], await db.Users.Select(u => u.Id).ToListAsync());
            Assert.All(await db.Sessions.ToListAsync(), s => Assert.Equal(bob.User.Id, s.UserId));
            Assert.Empty(await db.Friendships.ToListAsync());
        }

        // The name and email are free again
        await SignUpAsync("alice");
    }

    private static HttpRequestMessage DeleteMe(string password) =>
        new(HttpMethod.Delete, "/v1/me") { Content = JsonContent.Create(new DeleteAccountRequest(password)) };
}
