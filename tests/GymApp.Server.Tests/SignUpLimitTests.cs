using System.Net;
using System.Net.Http.Json;
using GymApp.Client;
using GymApp.Contracts;
using GymApp.Server.Auth;
using GymApp.Server.Feedback;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GymApp.Server.Tests;

/// <summary>Sign-up limits (0.21.1): only created accounts count, per address or IPv6 /64, and a server-wide spike
/// goes to Discord.</summary>
public class SignUpLimitTests(PostgresFixture postgres) : ServerTest(postgres)
{
    private readonly DiscordRecorder _discord = new();

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton(sp => new DiscordFeedbackForwarder(sp.GetRequiredService<IServiceScopeFactory>(), _discord,
            new Uri("https://discord.com/api/webhooks/1/secret"), Clock, NullLogger<DiscordFeedbackForwarder>.Instance));

    private Task<HttpResponseMessage> RegisterAsync(string username, string? email = null) =>
        Client.PostAsJsonAsync("/v1/auth/register",
            new RegisterRequest(username, email ?? $"{username}@example.com", DefaultPassword, "phone"), ApiFormat.Json);

    [Fact] public async Task FailedAttempts_DontUseUpTheSignUps()
    {
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync("alice")).StatusCode);
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.Conflict, (await RegisterAsync("alice", $"other{i}@example.com")).StatusCode);

        // The old limit (5 attempts an hour) would have refused this one
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync("bobby")).StatusCode);
    }

    [Fact] public async Task CreatedAccounts_AreCappedPerAddress_ForAnHour()
    {
        for (var i = 0; i < SignUpLimiter.PerAddressPerHour; i++)
            Assert.Equal(HttpStatusCode.Created, (await RegisterAsync($"user{i}")).StatusCode);

        var refused = await RegisterAsync("onemore");
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(ApiResult.TooManyMessage, (await Api().RegisterAsync(new RegisterRequest("onemore", "onemore@example.com", DefaultPassword, "phone"))).Error);

        Clock.Fixed = DateTimeOffset.UtcNow + SignUpLimiter.Window;
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync("onemore")).StatusCode);
    }

    [Fact] public async Task ASpikeAcrossTheServer_PostsOneAlert_AndRefusesNobody()
    {
        var limiter = Services.GetRequiredService<SignUpLimiter>();
        for (var i = 0; i < SignUpLimiter.AlertPerHour - 1; i++) limiter.Record($"ip:203.0.113.{i % 250}");
        Assert.Empty(_discord.Posts);

        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync("alice")).StatusCode);
        var alert = Assert.Single(_discord.Posts);
        Assert.Contains($"{SignUpLimiter.AlertPerHour} accounts were created in the last hour", alert);

        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync("bobby")).StatusCode);
        Assert.Single(_discord.Posts);   // at most one an hour
    }

    [Fact] public async Task DiscordDown_StillCreatesTheAccount()
    {
        _discord.Status = HttpStatusCode.ServiceUnavailable;
        var limiter = Services.GetRequiredService<SignUpLimiter>();
        for (var i = 0; i < SignUpLimiter.AlertPerHour - 1; i++) limiter.Record("ip:203.0.113.1");

        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync("alice")).StatusCode);
        Assert.Single(_discord.Posts);
    }

    [Theory]
    [InlineData("203.0.113.7", "ip:203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "ip:203.0.113.7")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "ip:2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2::1", "ip:2001:db8:1:2::/64")]
    public void AddressKey_GroupsIPv6ByItsNetwork(string address, string key) =>
        Assert.Equal(key, RateLimits.AddressKey(IPAddress.Parse(address)));

    [Fact] public void Limiter_FreesASignUp_AnHourAfterIt()
    {
        var clock = new TestClock { Fixed = new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero) };
        var limiter = new SignUpLimiter(clock);
        for (var i = 0; i < SignUpLimiter.PerAddressPerHour; i++) limiter.Record("ip:a");
        Assert.True(limiter.IsFull("ip:a"));
        Assert.False(limiter.IsFull("ip:b"));

        clock.Fixed += SignUpLimiter.Window - TimeSpan.FromSeconds(1);
        Assert.True(limiter.IsFull("ip:a"));
        clock.Fixed += TimeSpan.FromSeconds(1);
        Assert.False(limiter.IsFull("ip:a"));
    }

    /// <summary>Discord's webhook as the forwarder sees it: the text of each post.</summary>
    private sealed class DiscordRecorder : HttpMessageHandler, IHttpClientFactory
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.NoContent;
        public List<string> Posts { get; } = [];

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Posts.Add(await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(Status);
        }
    }
}
