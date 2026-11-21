using System.Net;
using GymApp.Contracts;

namespace GymApp.Server.Tests;

/// <summary>Phase 25d and installed apps: a server that accepts 0.21.0 (the frozen desktop) lets it through to the new
/// routes, and the app's own client speaks them.</summary>
public class BadgeCompatTests(PostgresFixture postgres) : ServerTest(postgres)
{
    protected override string? MinClientVersion => "0.21.0";

    [Fact] public async Task The021Desktop_IsNotToldToUpdate_OnTheNewRoutes()
    {
        var registered = await Api(appVersion: "0.21.0")
            .RegisterAsync(new RegisterRequest("alice", "alice@example.com", DefaultPassword, "desktop"));
        Assert.True(registered.IsOk, registered.Error);
        var client = ClientWith(registered.Value!.Token);
        foreach (var (method, path) in new[]
                 {
                     (HttpMethod.Get, "/v1/me/invites/accepted"),
                     (HttpMethod.Get, $"/v1/leaderboard?period=lastWeek&today={DateTime.UtcNow:yyyy-MM-dd}"),
                     (HttpMethod.Get, $"/v1/friends/{registered.Value.User.Id + 1}/badges"),
                 })
        {
            var request = new HttpRequestMessage(method, path);
            request.Headers.Add(ClientVersions.Header, "0.21.0");
            var response = await client.SendAsync(request);
            Assert.NotEqual(HttpStatusCode.UpgradeRequired, response.StatusCode);
        }
    }
}

/// <summary>Phase 25d: the app's own client speaks the badge routes.</summary>
public class BadgeClientTests(PostgresFixture postgres) : ServerTest(postgres)
{
    [Fact] public async Task TheAppsClient_SpeaksTheBadgeRoutes()
    {
        var alice = await PhoneAsync("alice");
        var bob = await PhoneAsync("bob");
        await alice.Friends.SendRequestAsync(alice.Me, bob.ServerId);
        await bob.Friends.AcceptAsync(bob.Me, alice.ServerId);

        var upload = await alice.Api.UploadBadgesAsync(new BadgesUploadRequest([new("streak", 2, new DateOnly(2026, 10, 1))], "streak", true));
        Assert.True(upload.IsOk, upload.Error);
        var seen = await bob.Api.GetFriendBadgesAsync(alice.ServerId);
        Assert.True(seen.IsOk, seen.Error);
        Assert.Equal("streak", seen.Value!.Featured);
        var invites = await alice.Api.GetAcceptedInvitesAsync();
        Assert.True(invites.IsOk, invites.Error);
        Assert.Equal(0, invites.Value!.Count);
        var lastWeek = await alice.Api.GetLeaderboardAsync(LeaderboardPeriod.LastWeek, GymApp.Core.AppClock.Today);
        Assert.True(lastWeek.IsOk, lastWeek.Error);
        Assert.Equal(LeaderboardPeriod.LastWeek, lastWeek.Value!.Period);
        Assert.Equal("streak:2", lastWeek.Value.Rows.Single(r => r.IsYou).FeaturedBadge);
    }
}
