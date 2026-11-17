using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using GymApp.Contracts;
using GymApp.Server.Data;
using GymApp.Server.Email;
using GymApp.Server.Push;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GymApp.Server.Tests;

/// <summary>Push tokens and the friend notifications (Phase 22a), with fake APNs and FCM senders.</summary>
public class PushTests(PostgresFixture postgres) : ServerTest(postgres)
{
    /// <summary>A push service that records what it was asked to send. A token can be made to hang, throw or be dead.</summary>
    private sealed class FakeSender(PushPlatform platform) : IPushSender
    {
        public PushPlatform Platform => platform;
        public readonly List<(string Token, bool Sandbox, PushMessage Message)> Sent = [];
        public readonly HashSet<string> Dead = [], Hanging = [], Throwing = [];

        public async Task<PushSendResult> SendAsync(PushTarget target, PushMessage message, CancellationToken ct)
        {
            // Ignores the token on purpose: the notifier must stop waiting on its own
            if (Hanging.Contains(target.Token)) await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None);
            if (Throwing.Contains(target.Token)) throw new HttpRequestException("APNs is down");
            lock (Sent) Sent.Add((target.Token, target.Sandbox, message));
            return Dead.Contains(target.Token) ? PushSendResult.DeadToken : PushSendResult.Sent;
        }
    }

    private sealed class Outbox : IEmailSender
    {
        public string LastBody = "";
        public Task<bool> SendAsync(string to, string subject, string body) { LastBody = body; return Task.FromResult(true); }
    }

    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(800);
    private readonly FakeSender _apns = new(PushPlatform.Apns);
    private readonly FakeSender _fcm = new(PushPlatform.Fcm);
    private readonly Outbox _mail = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IEmailSender>(_mail);
        services.AddSingleton(sp => new FriendNotifier(sp.GetRequiredService<IDbContextFactory<ServerDbContext>>(),
            [_apns, _fcm], Clock, NullLogger<FriendNotifier>.Instance) { TimeLimit = Limit });
    }

    private static Task<HttpResponseMessage> RegisterAsync(SignedIn user, string token,
        PushPlatform platform = PushPlatform.Apns, bool sandbox = false) =>
        user.Client.PutAsJsonAsync("/v1/me/push-device", new PushDeviceRequest(platform, token, sandbox), ApiFormat.Json);

    private static async Task RegisterOkAsync(SignedIn user, string token, PushPlatform platform = PushPlatform.Apns) =>
        Assert.Equal(HttpStatusCode.NoContent, (await RegisterAsync(user, token, platform)).StatusCode);

    private async Task<List<PushDevice>> DevicesAsync()
    {
        await using var db = Db();
        return await db.PushDevices.OrderBy(d => d.Id).ToListAsync();
    }

    private List<(string Token, PushMessage Message)> AllSent() =>
        _apns.Sent.Select(s => (s.Token, s.Message)).Concat(_fcm.Sent.Select(s => (s.Token, s.Message))).ToList();

    private static async Task<HttpStatusCode> PostAsync(SignedIn user, string url) => (await user.Client.PostAsync(url, null)).StatusCode;

    // --- Registering ---

    [Fact] public async Task Register_ReplacesThisSessionsToken_AndUnregisterRemovesIt()
    {
        var alice = await SignUpAsync("alice");
        await RegisterOkAsync(alice, "aaaa1111");
        var first = Assert.Single(await DevicesAsync());
        Assert.Equal((alice.User.Id, PushPlatform.Apns, "aaaa1111", false), (first.UserId, first.Platform, first.Token, first.Sandbox));

        Assert.Equal(HttpStatusCode.NoContent, (await RegisterAsync(alice, "fcm-token:xyz", PushPlatform.Fcm, sandbox: true)).StatusCode);
        var second = Assert.Single(await DevicesAsync());
        Assert.Equal((first.Id, PushPlatform.Fcm, "fcm-token:xyz"), (second.Id, second.Platform, second.Token));
        Assert.False(second.Sandbox);   // FCM has no sandbox

        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.DeleteAsync("/v1/me/push-device")).StatusCode);
        Assert.Empty(await DevicesAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.DeleteAsync("/v1/me/push-device")).StatusCode);
    }

    [Fact] public async Task Register_KeepsApnsSandbox_AndOneTokenPerSession()
    {
        var alice = await SignUpAsync("alice");
        var alicesIpad = await LogInAsync("alice");
        await RegisterOkAsync(alice, "phone");
        Assert.Equal(HttpStatusCode.NoContent, (await RegisterAsync(alicesIpad, "ipad", sandbox: true)).StatusCode);
        Assert.Equal([("phone", false), ("ipad", true)], (await DevicesAsync()).Select(d => (d.Token, d.Sandbox)));
    }

    [Fact] public async Task BadTokens_Are400()
    {
        var alice = await SignUpAsync("alice");
        foreach (var token in new[] { "", "has space", "line\nbreak", "é", new string('a', PushRules.MaxTokenLength + 1) })
            Assert.Equal(PushRules.BadTokenMessage, await ProblemAsync(await RegisterAsync(alice, token), HttpStatusCode.BadRequest));
        await RegisterOkAsync(alice, new string('a', PushRules.MaxTokenLength));

        var unknownPlatform = await alice.Client.PutAsync("/v1/me/push-device",
            new StringContent("""{"platform":"hms","token":"abc"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, unknownPlatform.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PutAsJsonAsync("/v1/me/push-device",
            new PushDeviceRequest(PushPlatform.Apns, "abc"), ApiFormat.Json)).StatusCode);
    }

    [Fact] public async Task SameToken_MovesToTheNewerSession_SoTheOldAccountGetsNothingThere()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bobby");
        var carol = await SignUpAsync("carol");
        await RegisterOkAsync(alice, "shared-phone");
        await RegisterOkAsync(bob, "shared-phone");   // bob signed in on alice's old phone

        var device = Assert.Single(await DevicesAsync());
        Assert.Equal(bob.User.Id, device.UserId);

        Assert.Equal(HttpStatusCode.OK, (await carol.Client.PostAsync($"/v1/friends/{alice.User.Id}/request", null)).StatusCode);
        Assert.Empty(AllSent());
    }

    [Fact] public async Task Logout_PasswordChange_Reset_AndDeletion_RemoveTheTokens()
    {
        var alice = await SignUpAsync("alice");
        var other = await LogInAsync("alice");
        await RegisterOkAsync(alice, "one");
        await RegisterOkAsync(other, "two");

        await PostAsync(other, "/v1/auth/logout");
        Assert.Equal(["one"], (await DevicesAsync()).Select(d => d.Token));

        var third = await LogInAsync("alice");
        await RegisterOkAsync(third, "three");
        Assert.Equal(HttpStatusCode.NoContent,
            (await alice.Client.PutAsJsonAsync("/v1/me/password", new ChangePasswordRequest(DefaultPassword, "brand new pass"))).StatusCode);
        Assert.Equal(["one"], (await DevicesAsync()).Select(d => d.Token));   // the other sessions ended

        await Client.PostAsJsonAsync("/v1/auth/password-reset", new PasswordResetRequest("alice@example.com"), ApiFormat.Json);
        var code = Regex.Match(_mail.LastBody, @"\b\d{6}\b").Value;
        Assert.Equal(HttpStatusCode.NoContent, (await Client.PostAsJsonAsync("/v1/auth/password-reset/confirm",
            new PasswordResetConfirmRequest("alice@example.com", code, "another new one"), ApiFormat.Json)).StatusCode);
        Assert.Empty(await DevicesAsync());

        var bob = await SignUpAsync("bobby");
        await RegisterOkAsync(bob, "bobs");
        var delete = new HttpRequestMessage(HttpMethod.Delete, "/v1/me") { Content = JsonContent.Create(new DeleteAccountRequest(DefaultPassword)) };
        Assert.Equal(HttpStatusCode.NoContent, (await bob.Client.SendAsync(delete)).StatusCode);
        Assert.Empty(await DevicesAsync());
    }

    [Fact] public async Task Registering_IsRateLimited()
    {
        var alice = await SignUpAsync("alice");
        for (var i = 0; i < 20; i++) await RegisterOkAsync(alice, $"token{i}");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await RegisterAsync(alice, "one-more")).StatusCode);
    }

    // --- What notifies ---

    [Fact] public async Task Request_NotifiesTheReceiver_OnEveryDevice_AndAcceptNotifiesTheRequester()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bobby");
        var bobsTablet = await LogInAsync("bobby");
        await RegisterOkAsync(alice, "alice-phone", PushPlatform.Fcm);
        await RegisterOkAsync(bob, "bob-phone");
        await RegisterOkAsync(bobsTablet, "bob-tablet", PushPlatform.Fcm);

        Assert.Equal(HttpStatusCode.OK, await PostAsync(alice, $"/v1/friends/{bob.User.Id}/request"));
        Assert.Equal(["bob-phone", "bob-tablet"], AllSent().Select(s => s.Token).Order());
        var message = AllSent()[0].Message;
        Assert.Equal(new PushMessage(PushKind.FriendRequest, alice.User.Id, "alice"), message);
        Assert.Equal(("Friend request", "alice wants to be your friend."), (message.Title, message.Body));
        Assert.Equal($"friendRequest-{alice.User.Id}", message.CollapseKey);
        Assert.Equal("friendRequest", message.Data[PushPayload.Kind]);
        Assert.Equal(alice.User.Id.ToString(), message.Data[PushPayload.Sender]);

        _apns.Sent.Clear(); _fcm.Sent.Clear();
        Assert.Equal(HttpStatusCode.NoContent, await PostAsync(bob, $"/v1/friends/{alice.User.Id}/accept"));
        var accepted = Assert.Single(AllSent());
        Assert.Equal(("alice-phone", new PushMessage(PushKind.FriendAccepted, bob.User.Id, "bobby")), accepted);
        Assert.Equal(("Request accepted", "bobby accepted your friend request."), (accepted.Message.Title, accepted.Message.Body));
    }

    [Fact] public async Task RequestingBack_AcceptsTheirs_AndTellsThemItWasAccepted()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bobby");
        await RegisterOkAsync(alice, "alice-phone");
        await PostAsync(alice, $"/v1/friends/{bob.User.Id}/request");

        await PostAsync(bob, $"/v1/friends/{alice.User.Id}/request");
        Assert.Equal([("alice-phone", new PushMessage(PushKind.FriendAccepted, bob.User.Id, "bobby"))], AllSent());
    }

    [Fact] public async Task AcceptingAnInvite_TellsTheInviter()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bobby");
        await RegisterOkAsync(alice, "alice-phone");
        await RegisterOkAsync(bob, "bob-phone");
        var invite = (await alice.Client.GetFromJsonAsync<FriendInviteResponse>("/v1/friends/invite", ApiFormat.Json))!;

        Assert.Equal(HttpStatusCode.OK, await PostAsync(bob, $"/v1/invites/{invite.Code}/accept"));
        var sent = Assert.Single(AllSent());
        Assert.Equal(("alice-phone", new PushMessage(PushKind.InviteAccepted, bob.User.Id, "bobby")), sent);
        Assert.Equal(("New friend", "bobby added you with your QR code. You're now friends."), (sent.Message.Title, sent.Message.Body));
    }

    [Fact] public async Task AcceptingAnInvite_WithARequestWaiting_StillTellsTheInviter()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bobby");
        await RegisterOkAsync(alice, "alice-phone");
        await PostAsync(alice, $"/v1/friends/{bob.User.Id}/request");
        var invite = (await alice.Client.GetFromJsonAsync<FriendInviteResponse>("/v1/friends/invite", ApiFormat.Json))!;

        Assert.Equal(HttpStatusCode.OK, await PostAsync(bob, $"/v1/invites/{invite.Code}/accept"));
        Assert.Equal([PushKind.InviteAccepted], AllSent().Select(s => s.Message.Kind));
    }

    [Fact] public async Task Decline_Cancel_Remove_Block_AndRefusals_NotifyNobody()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bobby");
        await RegisterOkAsync(alice, "alice-phone");
        await RegisterOkAsync(bob, "bob-phone");

        await PostAsync(alice, $"/v1/friends/{bob.User.Id}/request");
        _apns.Sent.Clear();
        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync(alice, $"/v1/friends/{bob.User.Id}/request"));   // already sent
        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync(alice, $"/v1/friends/{alice.User.Id}/request"));  // yourself
        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync(alice, $"/v1/friends/{bob.User.Id}/accept"));   // your own request
        Assert.Equal(HttpStatusCode.NoContent, await PostAsync(bob, $"/v1/friends/{alice.User.Id}/decline"));

        await PostAsync(alice, $"/v1/friends/{bob.User.Id}/request");   // within 12 hours: no second push
        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.DeleteAsync($"/v1/friends/{bob.User.Id}/request")).StatusCode);
        await PostAsync(alice, $"/v1/friends/{bob.User.Id}/request");
        Assert.Empty(AllSent());
        await PostAsync(bob, $"/v1/friends/{alice.User.Id}/accept");
        _apns.Sent.Clear();

        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.DeleteAsync($"/v1/friends/{bob.User.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await bob.Client.PutAsync($"/v1/blocks/{alice.User.Id}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, await PostAsync(alice, $"/v1/friends/{bob.User.Id}/request"));
        Assert.Empty(AllSent());
    }

    [Fact] public async Task RequestPushes_RepeatOnlyAfter12Hours()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bobby");
        await RegisterOkAsync(bob, "bob-phone");
        async Task RequestThenCancel()
        {
            await PostAsync(alice, $"/v1/friends/{bob.User.Id}/request");
            await alice.Client.DeleteAsync($"/v1/friends/{bob.User.Id}/request");
        }

        var start = DateTimeOffset.UtcNow;
        Clock.Fixed = start;
        await RequestThenCancel();
        await RequestThenCancel();
        Clock.Fixed = start + FriendNotifier.RequestRepeatAfter - TimeSpan.FromMinutes(1);
        await RequestThenCancel();
        Assert.Single(_apns.Sent);

        Clock.Fixed = start + FriendNotifier.RequestRepeatAfter;
        await RequestThenCancel();
        Assert.Equal(2, _apns.Sent.Count);
    }

    // --- The push-test command ---

    private async Task<(bool Ok, string Message)> PushTestAsync(string username)
    {
        await using var db = Db();
        return await PushCommands.RunAsync(["push-test", username], db, Services.GetRequiredService<FriendNotifier>());
    }

    [Fact] public async Task PushTest_SendsTheTestText_AndSaysHowEachDeviceWent_WithoutTokens()
    {
        var alice = await SignUpAsync("alice");
        var tablet = await LogInAsync("alice");
        await RegisterOkAsync(alice, "secret-phone-token");
        await RegisterOkAsync(tablet, "secret-tablet-token", PushPlatform.Fcm);
        _fcm.Dead.Add("secret-tablet-token");

        var (ok, message) = await PushTestAsync("ALICE");
        Assert.True(ok);
        Assert.Contains("2 of 2 device(s) tried", message);
        Assert.Contains("Apns, sent", message);
        Assert.Contains("Fcm, token no longer valid (deleted)", message);
        Assert.DoesNotContain("secret", message);
        var (_, sent) = Assert.Single(_apns.Sent.Select(s => (s.Token, s.Message)));
        Assert.Equal(("GainStreak", PushCommands.TestBody), (sent.Title, sent.Body));
        Assert.Equal(PushKind.FriendRequest, sent.Kind);
    }

    [Fact] public async Task PushTest_UnknownUser_OrNoDevices_IsNotOk()
    {
        await SignUpAsync("alice");
        Assert.Equal((false, "No user is called nobody."), await PushTestAsync("nobody"));
        var (ok, message) = await PushTestAsync("alice");
        Assert.False(ok);
        Assert.Contains("no phone registered", message);
    }

    // --- When sending goes wrong ---

    [Fact] public async Task AHangingOrFailingService_NeverChangesTheAnswer()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bobby");
        var bobsTablet = await LogInAsync("bobby");
        await RegisterOkAsync(bob, "slow");
        await RegisterOkAsync(bobsTablet, "broken", PushPlatform.Fcm);
        _apns.Hanging.Add("slow");
        _fcm.Throwing.Add("broken");

        var watch = Stopwatch.StartNew();
        var response = await alice.Client.PostAsync($"/v1/friends/{bob.User.Id}/request", null);
        watch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await response.Content.ReadFromJsonAsync<SendRequestResponse>(ApiFormat.Json))!.BecameFriends);
        Assert.InRange(watch.Elapsed, Limit, Limit + TimeSpan.FromSeconds(2));
        Assert.Equal(2, (await DevicesAsync()).Count);   // failures keep the tokens
    }

    [Fact] public async Task ADeadToken_IsDeleted_AndTheOthersKept()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bobby");
        var bobsTablet = await LogInAsync("bobby");
        await RegisterOkAsync(bob, "gone");
        await RegisterOkAsync(bobsTablet, "fine", PushPlatform.Fcm);
        _apns.Dead.Add("gone");

        await PostAsync(alice, $"/v1/friends/{bob.User.Id}/request");
        Assert.Equal(["fine"], (await DevicesAsync()).Select(d => d.Token));
    }

    [Fact] public async Task ExpiredSessions_GetNothing_AndAreTidiedOnTheNextRegister()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bobby");
        var bobsOld = await LogInAsync("bobby");
        await RegisterOkAsync(bobsOld, "old-phone");
        await using (var db = Db())
        {
            var oldSession = (await db.PushDevices.SingleAsync()).SessionId;
            await db.Sessions.Where(s => s.Id == oldSession).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        }

        await PostAsync(alice, $"/v1/friends/{bob.User.Id}/request");
        Assert.Empty(AllSent());

        await RegisterOkAsync(bob, "new-phone");
        Assert.Equal(["new-phone"], (await DevicesAsync()).Select(d => d.Token));
    }
}

/// <summary>A server with no push service configured, as production runs until the keys are set.</summary>
public class PushUnconfiguredTests(PostgresFixture postgres) : ServerTest(postgres)
{
    [Fact] public async Task TokensAreStillStored_AndFriendRequestsWorkAsBefore()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bobby");
        Assert.False(Services.GetRequiredService<FriendNotifier>().IsConfigured);
        Assert.Equal(HttpStatusCode.NoContent, (await bob.Client.PutAsJsonAsync("/v1/me/push-device",
            new PushDeviceRequest(PushPlatform.Fcm, "bob-phone"), ApiFormat.Json)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await alice.Client.PostAsync($"/v1/friends/{bob.User.Id}/request", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await bob.Client.PostAsync($"/v1/friends/{alice.User.Id}/accept", null)).StatusCode);
        await using var db = Db();
        Assert.Equal(1, await db.PushDevices.CountAsync());
        var (ok, message) = await PushCommands.RunAsync(["push-test", "bobby"], db, Services.GetRequiredService<FriendNotifier>());
        Assert.False(ok);
        Assert.Contains("No push service is configured", message);
    }
}

/// <summary>The server with both push services configured: it starts, registers both senders, and <c>push-test</c> works.</summary>
public class PushConfiguredTests(PostgresFixture postgres) : ServerTest(postgres)
{
    protected override IReadOnlyDictionary<string, string?> Settings => PushTestKeys.AllSettings();

    [Fact] public void BothSendersAreRegistered()
    {
        Assert.Equal([PushPlatform.Apns, PushPlatform.Fcm],
            Services.GetServices<IPushSender>().Select(s => s.Platform).Order());
        Assert.True(Services.GetRequiredService<FriendNotifier>().IsConfigured);
    }
}
