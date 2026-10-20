using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GymApp.Client;
using GymApp.Contracts;
using GymApp.Server.Data;
using GymApp.Server.Feedback;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GymApp.Server.Tests;

public class FeedbackEndpointTests(PostgresFixture postgres) : ServerTest(postgres)
{
    private static async Task<HttpResponseMessage> SendAsync(SignedIn user, FeedbackRequest request, string? version = "0.15.0")
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/v1/feedback") { Content = JsonContent.Create(request, options: ApiFormat.Json) };
        if (version is not null) message.Headers.Add(ClientVersions.Header, version);
        return await user.Client.SendAsync(message);
    }

    private List<FeedbackEntry> Stored()
    {
        using var db = Db();
        return db.Feedback.AsNoTracking().OrderBy(f => f.Id).ToList();
    }

    [Fact] public async Task Problem_IsStored_Scrubbed_WithTheAppVersion_AndQueuedForDiscord()
    {
        var alice = await SignUpAsync("alice");

        var response = await SendAsync(alice, new FeedbackRequest(FeedbackKind.Problem, "  The map froze  ", "at Foo in /Users/ana/GymApp/Foo.cs"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var row = Assert.Single(Stored());
        Assert.Equal((alice.User.Id, FeedbackKind.Problem, "The map froze", "0.15.0"), (row.UserId, row.Kind, row.Text, row.AppVersion));
        Assert.Equal("at Foo in /Users/<user>/GymApp/Foo.cs", row.Report);
        Assert.Null(row.ForwardedAt);
        Assert.True(Services.GetRequiredService<FeedbackQueue>().Reader.TryRead(out var queued));
        Assert.Equal(row.Id, queued);
    }

    [Fact] public async Task Invalid_IsRefused_WithTheMessage()
    {
        var alice = await SignUpAsync("alice");

        Assert.Equal(FeedbackRules.EmptyMessage,
            await ProblemAsync(await SendAsync(alice, new FeedbackRequest(FeedbackKind.Idea, "   ")), HttpStatusCode.BadRequest));
        Assert.Equal(FeedbackRules.TooLongMessage,
            await ProblemAsync(await SendAsync(alice, new FeedbackRequest(FeedbackKind.Idea, new string('a', FeedbackRules.MaxTextLength + 1))), HttpStatusCode.BadRequest));
        Assert.Equal(FeedbackRules.ReportOnIdeaMessage,
            await ProblemAsync(await SendAsync(alice, new FeedbackRequest(FeedbackKind.Idea, "x", "a report")), HttpStatusCode.BadRequest));
        Assert.Equal(FeedbackRules.ReportTooLongMessage,
            await ProblemAsync(await SendAsync(alice, new FeedbackRequest(FeedbackKind.Problem, "x", new string('r', FeedbackRules.MaxReportLength + 1))), HttpStatusCode.BadRequest));
        Assert.Empty(Stored());
    }

    [Fact] public async Task NeedsSignIn()
    {
        var response = await Client.PostAsJsonAsync("/v1/feedback", new FeedbackRequest(FeedbackKind.Idea, "hi"), ApiFormat.Json);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact] public async Task FivePerHourPerUser()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bob");
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(alice, new FeedbackRequest(FeedbackKind.Idea, $"idea {i}"))).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await SendAsync(alice, new FeedbackRequest(FeedbackKind.Idea, "one more"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(bob, new FeedbackRequest(FeedbackKind.Idea, "mine"))).StatusCode);
    }

    [Fact] public async Task DeletingTheAccount_DeletesItsFeedback()
    {
        var alice = await SignUpAsync("alice");
        var bob = await SignUpAsync("bob");
        await SendAsync(alice, new FeedbackRequest(FeedbackKind.Idea, "alice's"));
        await SendAsync(bob, new FeedbackRequest(FeedbackKind.Idea, "bob's"));

        var delete = new HttpRequestMessage(HttpMethod.Delete, "/v1/me") { Content = JsonContent.Create(new DeleteAccountRequest(DefaultPassword)) };
        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.SendAsync(delete)).StatusCode);

        Assert.Equal(["bob's"], Stored().Select(f => f.Text));
    }

    [Fact] public async Task TheAppsClient_SendsIt()
    {
        var tokens = new MemoryTokenStore();
        var api = Api(tokens, appVersion: "0.15.0");
        var auth = await api.RegisterAsync(new RegisterRequest("alice", "alice@example.com", DefaultPassword, "phone"));
        tokens.Set(auth.Value!.Token);

        Assert.True((await api.SendFeedbackAsync(new FeedbackRequest(FeedbackKind.Idea, "Dark mode"))).IsOk);
        Assert.Equal("0.15.0", Assert.Single(Stored()).AppVersion);
    }

    // --- Forwarding to Discord ---

    private sealed class Recorder : HttpMessageHandler, IHttpClientFactory
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.NoContent;
        public List<(Uri Url, MultipartFormDataContent Body, Dictionary<string, (string? FileName, string Text)> Parts)> Posts { get; } = new();

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = (MultipartFormDataContent)request.Content!;
            var parts = new Dictionary<string, (string?, string)>();
            foreach (var part in body)
                parts[part.Headers.ContentDisposition!.Name!.Trim('"')] =
                    (part.Headers.ContentDisposition.FileName?.Trim('"'), await part.ReadAsStringAsync(ct));
            Posts.Add((request.RequestUri!, body, parts));
            return new HttpResponseMessage(Status);
        }
    }

    private static readonly Uri Webhook = new("https://discord.com/api/webhooks/1/secret");

    private DiscordFeedbackForwarder Forwarder(Recorder recorder) => new(
        Services.GetRequiredService<FeedbackQueue>(), Services.GetRequiredService<IServiceScopeFactory>(), recorder, Webhook,
        Clock, NullLogger<DiscordFeedbackForwarder>.Instance);

    [Fact] public async Task Forward_PostsTheEmbed_AndTheReportFile_WithMentionsOff_AndMarksTheRow()
    {
        var alice = await SignUpAsync("alice");
        await SendAsync(alice, new FeedbackRequest(FeedbackKind.Problem, "@everyone the map froze", "the report"));
        var id = Assert.Single(Stored()).Id;
        var recorder = new Recorder();

        Assert.True(await Forwarder(recorder).ForwardAsync(id));

        var (url, _, parts) = Assert.Single(recorder.Posts);
        Assert.Equal(Webhook, url);
        using var payload = JsonDocument.Parse(parts["payload_json"].Text);
        Assert.Equal(0, payload.RootElement.GetProperty("allowed_mentions").GetProperty("parse").GetArrayLength());
        var embed = payload.RootElement.GetProperty("embeds")[0];
        Assert.Equal("Problem report", embed.GetProperty("title").GetString());
        Assert.Equal("@everyone the map froze", embed.GetProperty("description").GetString());
        Assert.Equal(["alice", "0.15.0", $"#{id}"], embed.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("value").GetString()));
        Assert.Equal(($"feedback-{id}.txt", "the report"), parts["files[0]"]);
        Assert.NotNull(Assert.Single(Stored()).ForwardedAt);
    }

    [Fact] public async Task Forward_AnIdea_HasNoFile_AndALongNoteGoesInTheFile()
    {
        var alice = await SignUpAsync("alice");
        await SendAsync(alice, new FeedbackRequest(FeedbackKind.Idea, "short idea"));
        await SendAsync(alice, new FeedbackRequest(FeedbackKind.Idea, new string('a', DiscordFeedbackForwarder.MaxEmbedText + 100)));
        var ids = Stored().Select(f => f.Id).ToList();
        var recorder = new Recorder();
        var forwarder = Forwarder(recorder);

        await forwarder.ForwardAsync(ids[0]);
        await forwarder.ForwardAsync(ids[1]);

        Assert.False(recorder.Posts[0].Parts.ContainsKey("files[0]"));
        Assert.Contains(new string('a', DiscordFeedbackForwarder.MaxEmbedText + 100), recorder.Posts[1].Parts["files[0]"].Text);
    }

    [Fact] public async Task Forward_Refused_LeavesTheRowUnforwarded()
    {
        var alice = await SignUpAsync("alice");
        await SendAsync(alice, new FeedbackRequest(FeedbackKind.Idea, "idea"));

        Assert.False(await Forwarder(new Recorder { Status = HttpStatusCode.BadRequest }).ForwardAsync(Assert.Single(Stored()).Id));
        Assert.Null(Assert.Single(Stored()).ForwardedAt);
    }

    [Fact] public void ReadWebhook_UnsetIsNull_AndNotHttpsThrows()
    {
        static IConfiguration Config(string? value) => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [DiscordFeedbackForwarder.SettingName] = value }).Build();

        Assert.Null(DiscordFeedbackForwarder.ReadWebhook(Config(null)));
        Assert.Equal(Webhook, DiscordFeedbackForwarder.ReadWebhook(Config(Webhook.ToString())));
        Assert.Throws<InvalidOperationException>(() => DiscordFeedbackForwarder.ReadWebhook(Config("http://discord.com/api/webhooks/1/x")));
        Assert.Throws<InvalidOperationException>(() => DiscordFeedbackForwarder.ReadWebhook(Config("not a url")));
    }
}
