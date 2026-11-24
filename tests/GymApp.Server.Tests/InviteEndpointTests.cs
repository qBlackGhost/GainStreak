using System.Net;
using System.Net.Http.Json;
using GymApp.Contracts;
using GymApp.Server.Endpoints;
using GymApp.Server.Friends;
using FriendService = GymApp.Social.FriendService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GymApp.Server.Tests;

/// <summary>Invite codes over the API, and the web side of invite links (Phase 21). The rules themselves are in the shared FriendServiceTests.</summary>
public class InviteEndpointTests(PostgresFixture postgres) : ServerTest(postgres)
{
    private const string AppStore = "https://testflight.apple.com/join/abc123";
    private const string TeamId = "ABCDE12345";
    private static readonly string Fingerprint = string.Join(':', Enumerable.Repeat("AB", 32));

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton(new InviteSettings(null, AppStore, InviteSettings.DefaultPlayStoreUrl, TeamId, [Fingerprint]));

    private static async Task<FriendInviteResponse> InviteOf(SignedIn user) =>
        (await user.Client.GetFromJsonAsync<FriendInviteResponse>("/v1/friends/invite", ApiFormat.Json))!;

    [Fact] public async Task FullFlow_ShowLookUpAccept()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bobby");

        var invite = await InviteOf(alice);
        Assert.Equal($"http://localhost/add/{invite.Code}", invite.Url);
        Assert.Equal(invite, await InviteOf(alice));

        var lookup = await bob.Client.GetFromJsonAsync<InviteLookupResponse>($"/v1/invites/{invite.Code}", ApiFormat.Json);
        Assert.Equal(new InviteLookupResponse(alice.User.Id, "alice", FriendRelation.None), lookup);

        var accept = await bob.Client.PostAsync($"/v1/invites/{invite.Code}/accept", null);
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        Assert.True((await accept.Content.ReadFromJsonAsync<SendRequestResponse>(ApiFormat.Json))!.BecameFriends);

        Assert.Equal(["bobby"], (await alice.Client.GetFromJsonAsync<List<FriendResponse>>("/v1/friends", ApiFormat.Json))!.Select(f => f.Username));
        Assert.Equal(FriendRelation.Friend,
            (await bob.Client.GetFromJsonAsync<InviteLookupResponse>($"/v1/invites/{invite.Code}", ApiFormat.Json))!.Relation);
        Assert.Equal("You're already friends.",
            await ProblemAsync(await bob.Client.PostAsync($"/v1/invites/{invite.Code}/accept", null), HttpStatusCode.BadRequest));
    }

    [Fact] public async Task Reset_GivesANewCode_AndTheOldOneIsNotFound()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bobby");
        var old = await InviteOf(alice);

        var response = await alice.Client.PostAsync("/v1/friends/invite/reset", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fresh = (await response.Content.ReadFromJsonAsync<FriendInviteResponse>(ApiFormat.Json))!;
        Assert.NotEqual(old.Code, fresh.Code);
        Assert.Equal(fresh, await InviteOf(alice));

        Assert.Equal(FriendService.UnknownInviteError,
            await ProblemAsync(await bob.Client.GetAsync($"/v1/invites/{old.Code}"), HttpStatusCode.NotFound));
        Assert.Equal(FriendService.UnknownInviteError,
            await ProblemAsync(await bob.Client.PostAsync($"/v1/invites/{old.Code}/accept", null), HttpStatusCode.NotFound));
    }

    [Fact] public async Task OwnCode_IsABadRequest()
    {
        var alice = await SignUpAsync("alice");
        var code = (await InviteOf(alice)).Code;
        Assert.Equal(FriendService.OwnInviteError,
            await ProblemAsync(await alice.Client.GetAsync($"/v1/invites/{code}"), HttpStatusCode.BadRequest));
    }

    [Fact] public async Task MalformedCode_IsNotFound()
    {
        var bob = await SignUpAsync("bobby");
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.GetAsync("/v1/invites/nope")).StatusCode);
    }

    [Fact] public async Task InviteEndpoints_NeedSignIn()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/v1/friends/invite")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/v1/invites/abcdefghjk")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsync("/v1/invites/abcdefghjk/accept", null)).StatusCode);
    }

    [Fact] public async Task LookingUpCodes_IsRateLimited()
    {
        var bob = await SignUpAsync("bobby");
        for (var i = 0; i < 30; i++)
            Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.GetAsync("/v1/invites/abcdefghjk")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await bob.Client.GetAsync("/v1/invites/abcdefghjk")).StatusCode);
    }

    // ---- the landing page ----

    private async Task<HttpResponseMessage> OpenLinkAsync(string code, string? userAgent)
    {
        var client = ClientWithoutRedirects();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/add/{code}");
        if (userAgent is not null) request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        return await client.SendAsync(request);
    }

    private const string IPhone = "Mozilla/5.0 (iPhone; CPU iPhone OS 26_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.1 Mobile/15E148 Safari/604.1";
    private const string Android = "Mozilla/5.0 (Linux; Android 16; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0 Mobile Safari/537.36";
    private const string Mac = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.1 Safari/605.1.15";

    private const string InstagramIPhone = "Mozilla/5.0 (iPhone; CPU iPhone OS 26_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148 Instagram 400.0.0.0.0";

    [Theory]
    [InlineData(IPhone)]
    [InlineData(InstagramIPhone)]
    public async Task Landing_ShowsAnIPhoneOpenInAppAndTheAppStore(string userAgent)
    {
        // Not a redirect: in Instagram's browser the app may well be installed (the OS never got the link)
        var response = await OpenLinkAsync("abcdefghjk", userAgent);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("href=\"gainstreak://add/abcdefghjk\"", html);
        Assert.Contains(AppStore, html);
        Assert.DoesNotContain(InviteSettings.DefaultPlayStoreUrl, html);
        Assert.DoesNotContain("<svg", html);
    }

    [Fact] public async Task Landing_ShowsAnAndroidPhoneAnIntentWithGooglePlayAsFallback()
    {
        var response = await OpenLinkAsync("abcdefghjk", Android);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("intent://add/abcdefghjk#Intent;scheme=gainstreak;package=com.gainstreak.app;S.browser_fallback_url=" +
                        Uri.EscapeDataString(InviteSettings.DefaultPlayStoreUrl), System.Net.WebUtility.HtmlDecode(html));
        Assert.Contains(InviteSettings.DefaultPlayStoreUrl, System.Net.WebUtility.HtmlDecode(html));
        Assert.DoesNotContain(AppStore, html);
    }

    [Fact] public async Task Landing_ShowsAComputerTheQrCode_WithoutWhoseItIs()
    {
        var alice = await SignUpAsync("alice");
        var code = (await InviteOf(alice)).Code;

        var response = await OpenLinkAsync(code, Mac);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<svg", html);
        Assert.Contains($"localhost/add/{code}", html);
        Assert.Contains(InviteLinkEndpoints.PublicPages + "privacy.html", html);
        Assert.Contains(AppStore, html);
        Assert.Contains(InviteSettings.DefaultPlayStoreUrl, html);
        Assert.DoesNotContain("alice", html);
    }

    [Fact] public async Task Landing_MalformedCode_IsNotFound() =>
        Assert.Equal(HttpStatusCode.NotFound, (await OpenLinkAsync("not-a-code", Mac)).StatusCode);

    [Fact] public async Task AppleAppSiteAssociation_AllowsInviteLinks()
    {
        var response = await Client.GetAsync("/.well-known/apple-app-site-association");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("""{"applinks":{"details":[{"appIDs":["ABCDE12345.com.gainstreak.app"],"components":[{"/":"/add/*"}]}]}}""",
            await response.Content.ReadAsStringAsync());
    }

    [Fact] public async Task AssetLinks_NameTheAppAndItsKeys()
    {
        var response = await Client.GetAsync("/.well-known/assetlinks.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal($$$"""[{"relation":["delegate_permission/common.handle_all_urls"],"target":{"namespace":"android_app","package_name":"com.gainstreak.app","sha256_cert_fingerprints":["{{{Fingerprint}}}"]}}]""",
            await response.Content.ReadAsStringAsync());
    }
}

/// <summary>The web side with no invite settings: the server's defaults.</summary>
public class InviteDefaultsTests(PostgresFixture postgres) : ServerTest(postgres)
{
    [Fact] public async Task WellKnownFiles_AreNotFound_UntilConfigured()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/.well-known/apple-app-site-association")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/.well-known/assetlinks.json")).StatusCode);
    }

    [Fact] public async Task Landing_WithNoAppStoreLink_ShowsAnIPhoneThePage()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/add/abcdefghjk");
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (iPhone; CPU iPhone OS 26_1 like Mac OS X)");
        var response = await ClientWithoutRedirects().SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("App Store: coming soon", await response.Content.ReadAsStringAsync());
    }
}

/// <summary>Reading the Invites settings; no server needed.</summary>
public class InviteSettingsTests
{
    private static InviteSettings Read(params (string Key, string Value)[] values) =>
        InviteSettings.Read(new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>($"Invites:{v.Key}", v.Value)))
            .Build());

    [Fact] public void Unset_IsAllDefaults()
    {
        var s = Read();
        Assert.Equal((null, null, InviteSettings.DefaultPlayStoreUrl, null), (s.BaseUrl, s.AppStoreUrl, s.PlayStoreUrl, s.AppleTeamId));
        Assert.Empty(s.AndroidCertFingerprints);
    }

    [Fact] public void ReadsEverything()
    {
        var fp = string.Join(':', Enumerable.Repeat("ab", 32));
        var s = Read(("BaseUrl", "https://gainstreak.app/"), ("AppStoreUrl", "https://apps.apple.com/app/id1"),
            ("PlayStoreUrl", "https://play.google.com/apps/testing/com.gainstreak.app"), ("AppleTeamId", "ABCDE12345"),
            ("AndroidCertFingerprints", $"{fp}; {fp.ToUpperInvariant()}"));
        Assert.Equal("https://gainstreak.app/", s.BaseUrl!.ToString());
        Assert.Equal("https://apps.apple.com/app/id1", s.AppStoreUrl);
        Assert.Equal("https://play.google.com/apps/testing/com.gainstreak.app", s.PlayStoreUrl);
        Assert.Equal("ABCDE12345", s.AppleTeamId);
        Assert.Equal([fp.ToUpperInvariant(), fp.ToUpperInvariant()], s.AndroidCertFingerprints);
    }

    [Theory]
    [InlineData("BaseUrl", "http://gainstreak.app")]
    [InlineData("BaseUrl", "https://gainstreak.app/invites")]
    [InlineData("BaseUrl", "gainstreak.app")]
    [InlineData("AppStoreUrl", "itms-apps://apps.apple.com/app/id1")]
    [InlineData("PlayStoreUrl", "market://details?id=com.gainstreak.app")]
    [InlineData("AppleTeamId", "abc")]
    [InlineData("AndroidCertFingerprints", "AB:CD")]
    public void Malformed_IsAStartupError(string key, string value) =>
        Assert.Throws<InvalidOperationException>(() => Read((key, value)));

    [Theory]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 26_1 like Mac OS X)", InviteLinkEndpoints.Store.AppStore)]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 18_0 like Mac OS X)", InviteLinkEndpoints.Store.AppStore)]
    [InlineData("Mozilla/5.0 (Linux; Android 16; Pixel 9)", InviteLinkEndpoints.Store.Play)]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)", InviteLinkEndpoints.Store.None)]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64)", InviteLinkEndpoints.Store.None)]
    [InlineData(null, InviteLinkEndpoints.Store.None)]
    public void StoreFor_UserAgent(string? userAgent, InviteLinkEndpoints.Store expected) =>
        Assert.Equal(expected, InviteLinkEndpoints.StoreFor(userAgent));
}
