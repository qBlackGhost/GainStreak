using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using GymApp.Client;
using GymApp.Contracts;
using GymApp.Server.Data;
using GymApp.Server.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GymApp.Server.Tests;

public class EmailCodeTests(PostgresFixture postgres) : ServerTest(postgres)
{
    private sealed class Outbox : IEmailSender
    {
        public bool Fails;
        public List<(string To, string Subject, string Body)> Sent { get; } = new();

        public Task<bool> SendAsync(string to, string subject, string body)
        {
            if (Fails) return Task.FromResult(false);
            Sent.Add((to, subject, body));
            return Task.FromResult(true);
        }
    }

    private readonly Outbox _mail = new();

    protected override void ConfigureServices(IServiceCollection services) => services.AddSingleton<IEmailSender>(_mail);

    private string LastCode() => Regex.Match(_mail.Sent[^1].Body, @"\b\d{6}\b").Value;
    private static string Wrong(string code) => code == "000000" ? "000001" : "000000";

    private static Task<HttpResponseMessage> ConfirmAsync(SignedIn user, string code) =>
        user.Client.PostAsJsonAsync("/v1/me/email/confirm", new ConfirmEmailRequest(code), ApiFormat.Json);

    private Task<HttpResponseMessage> RequestResetAsync(string email) =>
        Client.PostAsJsonAsync("/v1/auth/password-reset", new PasswordResetRequest(email), ApiFormat.Json);

    private Task<HttpResponseMessage> ResetAsync(string email, string code, string password = "a brand new one") =>
        Client.PostAsJsonAsync("/v1/auth/password-reset/confirm", new PasswordResetConfirmRequest(email, code, password), ApiFormat.Json);

    private Task<HttpResponseMessage> LoginAsync(string password) =>
        Client.PostAsJsonAsync("/v1/auth/login", new LoginRequest("alice", password, "phone"), ApiFormat.Json);

    private static async Task<MeResponse> MeAsync(SignedIn user) =>
        (await user.Client.GetFromJsonAsync<MeResponse>("/v1/me", ApiFormat.Json))!;

    [Fact] public async Task SignUp_EmailsACode_AndConfirmingIsRemembered()
    {
        var alice = await SignUpAsync("alice");

        Assert.Equal("alice@example.com", Assert.Single(_mail.Sent).To);
        Assert.False(alice.User.EmailConfirmed);

        Assert.Equal(HttpStatusCode.NoContent, (await ConfirmAsync(alice, LastCode())).StatusCode);

        Assert.True((await MeAsync(alice)).EmailConfirmed);
    }

    [Fact] public async Task SignUp_StillWorks_WhenTheEmailCantBeSent()
    {
        _mail.Fails = true;

        var alice = await SignUpAsync("alice");

        Assert.False(alice.User.EmailConfirmed);
        var resend = await alice.Client.PostAsync("/v1/me/email/code", null);
        Assert.Equal(HttpStatusCode.BadRequest, resend.StatusCode);
    }

    [Fact] public async Task TheCode_IsStoredOnlyAsAHash()
    {
        await SignUpAsync("alice");

        await using var db = Db();
        Assert.DoesNotContain(LastCode(), (await db.EmailCodes.SingleAsync()).CodeHash);
    }

    [Fact] public async Task WrongCodes_RunOutAfterFiveTries_EvenForTheRightOneAfter()
    {
        var alice = await SignUpAsync("alice");
        var code = LastCode();

        for (var i = 0; i < EmailCode.MaxAttempts; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await ConfirmAsync(alice, Wrong(code))).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await ConfirmAsync(alice, code)).StatusCode);
        Assert.False((await MeAsync(alice)).EmailConfirmed);
    }

    [Fact] public async Task AnExpiredCode_IsRefused()
    {
        var alice = await SignUpAsync("alice");
        await using (var db = Db())
            await db.EmailCodes.ExecuteUpdateAsync(c => c.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));

        Assert.Equal(HttpStatusCode.BadRequest, (await ConfirmAsync(alice, LastCode())).StatusCode);
    }

    [Fact] public async Task ACode_WorksOnce()
    {
        var alice = await SignUpAsync("alice");
        var code = LastCode();
        await ConfirmAsync(alice, code);

        Assert.Equal(HttpStatusCode.BadRequest, (await ConfirmAsync(alice, code)).StatusCode);
    }

    [Fact] public async Task AskingAgain_WaitsAMinute_AndReplacesTheCode()
    {
        var alice = await SignUpAsync("alice");
        var first = LastCode();

        Assert.Equal(HttpStatusCode.BadRequest, (await alice.Client.PostAsync("/v1/me/email/code", null)).StatusCode);
        Assert.Single(_mail.Sent);

        await using (var db = Db())
            await db.EmailCodes.ExecuteUpdateAsync(c => c.SetProperty(x => x.CreatedAt, x => x.CreatedAt.AddMinutes(-2)));
        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.PostAsync("/v1/me/email/code", null)).StatusCode);

        if (first != LastCode()) Assert.Equal(HttpStatusCode.BadRequest, (await ConfirmAsync(alice, first)).StatusCode);
    }

    [Fact] public async Task AConfirmedEmail_NeedsNoCode()
    {
        var alice = await SignUpAsync("alice");
        await ConfirmAsync(alice, LastCode());

        Assert.Equal(HttpStatusCode.Conflict, (await alice.Client.PostAsync("/v1/me/email/code", null)).StatusCode);
    }

    [Fact] public async Task ChangingTheEmail_NeedsConfirmingAgain_AtTheNewAddress()
    {
        var alice = await SignUpAsync("alice");
        await ConfirmAsync(alice, LastCode());

        var change = await alice.Client.PutAsJsonAsync("/v1/me/email", new ChangeEmailRequest(DefaultPassword, "new@example.com"), ApiFormat.Json);

        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        Assert.False((await MeAsync(alice)).EmailConfirmed);
        Assert.Equal("new@example.com", _mail.Sent[^1].To);
        Assert.Equal(HttpStatusCode.NoContent, (await ConfirmAsync(alice, LastCode())).StatusCode);
    }

    [Fact] public async Task Reset_SetsTheNewPassword_EndsEverySession_AndConfirmsTheEmail()
    {
        var alice = await SignUpAsync("alice");

        Assert.Equal(HttpStatusCode.NoContent, (await RequestResetAsync("ALICE@example.com")).StatusCode);
        Assert.Equal("Reset your GainStreak password", _mail.Sent[^1].Subject);
        Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync("alice@example.com", LastCode())).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await alice.Client.GetAsync("/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(DefaultPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync("a brand new one")).StatusCode);
        await using var db = Db();
        Assert.NotNull((await db.Users.SingleAsync()).EmailConfirmedAt);
    }

    [Fact] public async Task ResetRequest_LooksTheSame_WhetherOrNotTheAccountExists()
    {
        await SignUpAsync("alice");
        _mail.Sent.Clear();

        var unknown = await RequestResetAsync("nobody@example.com");

        Assert.Equal(HttpStatusCode.NoContent, unknown.StatusCode);
        Assert.Empty(_mail.Sent);
    }

    [Fact] public async Task Reset_RefusesAWrongCode_AndAWeakPassword_AndKeepsTheOldOne()
    {
        await SignUpAsync("alice");
        await RequestResetAsync("alice@example.com");
        var code = LastCode();

        Assert.Equal(HttpStatusCode.BadRequest, (await ResetAsync("alice@example.com", Wrong(code))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ResetAsync("alice@example.com", code, "short")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ResetAsync("nobody@example.com", code)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(DefaultPassword)).StatusCode);
    }

    [Fact] public async Task AConfirmCode_CantResetAPassword()
    {
        await SignUpAsync("alice");

        Assert.Equal(HttpStatusCode.BadRequest, (await ResetAsync("alice@example.com", LastCode())).StatusCode);
    }

    [Fact] public async Task ASuspendedAccount_GetsNoResetCode()
    {
        await SignUpAsync("alice");
        await using (var db = Db()) await AccountCommands.RunAsync(["suspend", "alice"], db, Clock);
        _mail.Sent.Clear();

        await RequestResetAsync("alice@example.com");

        Assert.Empty(_mail.Sent);
    }

    [Fact] public async Task TheAppsAccountService_ResetsThroughTheServer_AndUpdatesItsCachedPassword()
    {
        var phone = await PhoneAsync("alice");
        await phone.Accounts.LogOutAsync();

        Assert.True((await phone.Accounts.RequestPasswordResetAsync("alice@example.com")).Success);
        Assert.True((await phone.Accounts.ResetPasswordAsync("alice@example.com", LastCode(), "a brand new one")).Success);

        Assert.True((await phone.Accounts.SignInAsync("alice", "a brand new one")).Success);
    }

    [Fact] public async Task TheAppsAccountService_KnowsWhenTheEmailIsConfirmed()
    {
        var phone = await PhoneAsync("alice");
        Assert.False(phone.Accounts.EmailConfirmed);

        Assert.True((await phone.Accounts.ConfirmEmailAsync(LastCode())).Success);

        Assert.True(phone.Accounts.EmailConfirmed);
        await phone.Accounts.LogOutAsync();
        Assert.Null(phone.Accounts.EmailConfirmed);
    }
}
