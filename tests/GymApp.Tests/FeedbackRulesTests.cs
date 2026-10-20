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
}
