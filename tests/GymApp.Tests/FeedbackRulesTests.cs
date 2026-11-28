using System.Net;
using GymApp.Client;
using GymApp.Contracts;
using GymApp.Services;

namespace GymApp.Tests;

public class FeedbackRulesTests
{
    [Theory]
    [InlineData("at Foo() in /Users/cristi/GymApp/Foo.cs:line 3", "at Foo() in /Users/<user>/GymApp/Foo.cs:line 3")]
    [InlineData("/home/maria/.local/share/GymApp/errors.log", "/home/<user>/.local/share/GymApp/errors.log")]
    [InlineData(@"C:\Users\Maria Lopez\AppData", @"C:\Users\<user>\AppData")]
    [InlineData(@"c:\Users\bob\x", @"c:\Users\<user>\x")]
    [InlineData("/Users/<user>/already", "/Users/<user>/already")]
    [InlineData("no paths here", "no paths here")]
    public void Scrub_TakesTheUserNameOutOfPaths(string text, string expected) => Assert.Equal(expected, FeedbackRules.Scrub(text));
}

public class RemoteFeedbackServiceTests
{
    private readonly FakeSocialServer _server = new();

    [Fact] public async Task Sends_TheScrubbedNoteAndReport()
    {
        _server.On("POST v1/feedback", HttpStatusCode.NoContent);
        var r = await new RemoteFeedbackService(_server.Api).SendAsync(FeedbackKind.Problem, "  broke in /Users/ana/x  ", "at /Users/ana/y");

        Assert.True(r.Success);
        Assert.Equal(new FeedbackRequest(FeedbackKind.Problem, "broke in /Users/<user>/x", "at /Users/<user>/y"),
            _server.LastBody<FeedbackRequest>("POST v1/feedback"));
    }

    [Fact] public async Task Offline_OrRefused_ReturnsTheMessage()
    {
        var service = new RemoteFeedbackService(_server.Api);
        _server.Offline = true;
        Assert.Equal(ApiResult.OfflineMessage, (await service.SendAsync(FeedbackKind.Idea, "x", null)).Error);

        _server.Offline = false;
        _server.On("POST v1/feedback", HttpStatusCode.TooManyRequests);
        Assert.Equal(ApiResult.TooManyMessage, (await service.SendAsync(FeedbackKind.Idea, "x", null)).Error);
    }

    [Fact] public async Task EmptyOrTooLong_IsCheckedBeforeSending()
    {
        var service = new RemoteFeedbackService(_server.Api);
        Assert.Equal(FeedbackRules.EmptyMessage, (await service.SendAsync(FeedbackKind.Idea, "   ", null)).Error);
        Assert.Equal(FeedbackRules.TooLongMessage, (await service.SendAsync(FeedbackKind.Idea, new string('a', FeedbackRules.MaxTextLength + 1), null)).Error);
        Assert.Empty(_server.Calls);
    }

    // --- The 10-minute wait (0.28.1) ---

    private static SendCooldowns SignedIn()
    {
        var session = new SessionService();
        session.SignIn(new GymApp.Data.Models.User { Id = 1, Username = "ana" });
        return new SendCooldowns(session);
    }

    [Fact] public async Task ASend_StartsTheWait_ForFeedbackOnly()
    {
        var cooldowns = SignedIn();
        var service = new RemoteFeedbackService(_server.Api, cooldowns);
        _server.On("POST v1/feedback", HttpStatusCode.NoContent);
        Assert.Null(service.WaitUntil);

        Assert.True((await service.SendAsync(FeedbackKind.Idea, "x", null)).Success);

        Assert.InRange(service.WaitUntil!.Value - DateTime.UtcNow, TimeSpan.FromMinutes(9), FeedbackRules.Cooldown);
        Assert.Null(cooldowns.Until(CooldownKind.Report));
    }

    [Fact] public async Task TheServersRetryTime_IsTheWait()
    {
        var cooldowns = SignedIn();
        var retryAt = DateTime.UtcNow.AddMinutes(4);
        var body = "{\"status\":429,\"detail\":\"wait\",\"retryAt\":\"" + retryAt.ToString("O") + "\"}";
        _server.On("POST v1/feedback", _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/problem+json")
        });

        var r = await new RemoteFeedbackService(_server.Api, cooldowns).SendAsync(FeedbackKind.Idea, "x", null);

        Assert.Equal("wait", r.Error);
        Assert.Equal(retryAt, cooldowns.Until(CooldownKind.Feedback)!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact] public async Task AReport_StartsItsOwnWait()
    {
        var cooldowns = SignedIn();
        _server.On("POST v1/users/5/report", HttpStatusCode.NoContent);
        var service = new RemoteUserReportService(_server.Api, cooldowns);

        Assert.Null(await service.ReportAsync(5, UserReportReason.Spam, null));

        Assert.NotNull(service.WaitUntil);
        Assert.Null(cooldowns.Until(CooldownKind.Feedback));
    }

    [Fact] public void TheWait_IsPerUser_AndEnds()
    {
        var session = new SessionService();
        var time = new StepClock();
        var cooldowns = new SendCooldowns(session, time);
        session.SignIn(new GymApp.Data.Models.User { Id = 1, Username = "ana" });
        cooldowns.Sent(CooldownKind.Feedback);
        Assert.NotNull(cooldowns.Until(CooldownKind.Feedback));

        session.SignIn(new GymApp.Data.Models.User { Id = 2, Username = "bob" });
        Assert.Null(cooldowns.Until(CooldownKind.Feedback));   // someone else on this phone

        session.SignIn(new GymApp.Data.Models.User { Id = 1, Username = "ana" });
        time.Now += FeedbackRules.Cooldown;
        Assert.Null(cooldowns.Until(CooldownKind.Feedback));
    }

    private sealed class StepClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 28, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact] public void Minutes_RoundUp_AndSayOne()
    {
        Assert.Equal("1 minute", Cooldowns.Minutes(TimeSpan.FromSeconds(5)));
        Assert.Equal("7 minutes", Cooldowns.Minutes(TimeSpan.FromMinutes(6.2)));
    }
}
