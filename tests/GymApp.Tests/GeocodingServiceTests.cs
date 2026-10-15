using System.Net;
using GymApp.Core;
using GymApp.Services;

namespace GymApp.Tests;

public class GeocodingServiceTests
{
    private static GeocodingService Svc(FakeHttpHandler h, int minIntervalMs = 0, TimeSpan? timeout = null) =>
        new(h, TimeSpan.FromMilliseconds(minIntervalMs), timeout);

    [Fact] public async Task Search_ReturnsCandidates_AndSendsUserAgentAndQuery()
    {
        var h = FakeHttpHandler.Json(Fixtures.Read("nominatim_timisoara.json"));
        var r = await Svc(h).SearchAsync("  Timișoara ");

        Assert.True(r.Success);
        Assert.Equal("Timișoara", Assert.Single(r.Candidates).Name);

        var req = Assert.Single(h.Requests);
        Assert.Equal(OsmHttp.UserAgent, req.Headers.UserAgent.ToString());
        var url = req.RequestUri!.AbsoluteUri;
        Assert.StartsWith("https://nominatim.openstreetmap.org/search?", url);
        Assert.Contains("q=Timi%C8%99oara&", url);   // trimmed and escaped
        Assert.Contains("format=jsonv2", url);
        Assert.Contains($"limit={Nominatim.RequestLimit}", url);   // more than 5, since counties get dropped
    }

    [Fact] public async Task NoMatches_IsASuccessWithNoCandidates()
    {
        var r = await Svc(FakeHttpHandler.Json("[]")).SearchAsync("Qwxzv");
        Assert.True(r.Success);
        Assert.Empty(r.Candidates);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyQuery_FailsWithoutARequest(string q)
    {
        var h = FakeHttpHandler.Json("[]");
        var r = await Svc(h).SearchAsync(q);
        Assert.False(r.Success);
        Assert.Empty(h.Requests);
    }

    [Fact] public async Task TooLongQuery_FailsWithoutARequest()
    {
        var h = FakeHttpHandler.Json("[]");
        Assert.False((await Svc(h).SearchAsync(new string('a', GeocodingService.MaxQueryLength + 1))).Success);
        Assert.Empty(h.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "Too many searches")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "HTTP 503")]
    public async Task HttpErrors_ComeBackAsAResult(HttpStatusCode status, string expected)
    {
        var r = await Svc(FakeHttpHandler.Json("", status)).SearchAsync("Timișoara");
        Assert.False(r.Success);
        Assert.Contains(expected, r.Error);
        Assert.Empty(r.Candidates);
    }

    [Fact] public async Task NetworkError_ComesBackAsAResult()
    {
        var r = await Svc(FakeHttpHandler.Throws(new HttpRequestException("no route"))).SearchAsync("Timișoara");
        Assert.False(r.Success);
        Assert.Contains("internet", r.Error);
    }

    [Fact] public async Task BrokenJson_ComesBackAsAResult()
    {
        var r = await Svc(FakeHttpHandler.Json("<html>oops</html>")).SearchAsync("Timișoara");
        Assert.False(r.Success);
        Assert.Contains("unexpected", r.Error);
    }

    [Fact] public async Task Timeout_ComesBackAsAResult()
    {
        var h = new FakeHttpHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var r = await Svc(h, timeout: TimeSpan.FromMilliseconds(100)).SearchAsync("Timișoara");
        Assert.False(r.Success);
        Assert.Contains("too long", r.Error);
    }

    [Fact] public async Task CallerCancellation_IsNotSwallowed()
    {
        var h = new FakeHttpHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var cts = new CancellationTokenSource(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Svc(h).SearchAsync("Timișoara", cts.Token));
    }

    [Fact] public async Task Searches_AreSpacedOutByTheMinimumInterval()
    {
        var h = FakeHttpHandler.Json("[]");
        var svc = Svc(h, minIntervalMs: 300);

        await Task.WhenAll(svc.SearchAsync("a"), svc.SearchAsync("b"), svc.SearchAsync("c"));

        Assert.Equal(3, h.SentAt.Count);
        var times = h.SentAt.Order().ToList();
        for (var i = 1; i < times.Count; i++)
            Assert.True(times[i] - times[i - 1] >= TimeSpan.FromMilliseconds(280), $"gap {i}: {times[i] - times[i - 1]}");
    }
}
