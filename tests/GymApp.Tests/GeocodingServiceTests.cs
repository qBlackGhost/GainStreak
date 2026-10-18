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
    [InlineData(HttpStatusCode.TooManyRequests, "Too many requests")]
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

    [Fact] public async Task NetworkError_CanBeRetried_ButABadQueryCant()
    {
        Assert.True((await Svc(FakeHttpHandler.Throws(new HttpRequestException("no route"))).SearchAsync("Timișoara")).CanRetry);
        Assert.True((await Svc(FakeHttpHandler.Throws(new HttpRequestException("no route")))
            .SearchAddressAsync("Calea Aradului", new GymApp.Data.Models.City { Name = "Timișoara", DisplayName = "Timișoara" })).CanRetry);
        Assert.False((await Svc(FakeHttpHandler.Json("[]")).SearchAsync("   ")).CanRetry);
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

    // ===== Address search and reverse lookup =====

    private static GymApp.Data.Models.City Timisoara() => new()
    {
        Name = "Timișoara", South = 45.6838246, West = 21.1116821, North = 45.8341735, East = 21.3272446
    };

    [Fact] public async Task AddressSearch_IsBoundedToTheCitysBox()
    {
        var h = FakeHttpHandler.Json(Fixtures.Read("nominatim_address_calea_aradului.json"));
        var r = await Svc(h).SearchAddressAsync(" Calea Aradului 1 ", Timisoara());

        Assert.True(r.Success);
        Assert.Equal("Calea Aradului 1, Circumvalațiunii", Assert.Single(r.Candidates).Text);
        var url = Uri.UnescapeDataString(Assert.Single(h.Requests).RequestUri!.AbsoluteUri);
        Assert.Contains("q=Calea Aradului 1&", url);
        Assert.Contains("viewbox=21.1116821,45.8341735,21.3272446,45.6838246", url);   // left,top,right,bottom
        Assert.Contains("bounded=1", url);
        Assert.Contains("addressdetails=1", url);
    }

    [Fact] public async Task AddressSearch_EmptyQuery_FailsWithoutARequest()
    {
        var h = FakeHttpHandler.Json("[]");
        Assert.False((await Svc(h).SearchAddressAsync("  ", Timisoara())).Success);
        Assert.Empty(h.Requests);
    }

    [Fact] public async Task Reverse_FindsTheAddress()
    {
        var h = FakeHttpHandler.Json(Fixtures.Read("nominatim_reverse_worldclass.json"));
        var r = await Svc(h).ReverseAsync(45.7658704, 21.2272598);

        Assert.True(r.Success);
        Assert.Equal("Piața Consiliul Europei, Lipovei", r.Address);
        var url = Assert.Single(h.Requests).RequestUri!.AbsoluteUri;
        Assert.Contains("/reverse?lat=45.7658704&lon=21.2272598", url);   // invariant culture
        Assert.Equal(OsmHttp.UserAgent, h.Requests[0].Headers.UserAgent.ToString());
    }

    [Fact] public async Task Reverse_NothingThere_IsASuccessWithNoAddress()
    {
        var r = await Svc(FakeHttpHandler.Json(Fixtures.Read("nominatim_reverse_nothing.json"))).ReverseAsync(45, 30);
        Assert.True(r.Success);
        Assert.Null(r.Address);
    }

    [Fact] public async Task Reverse_NetworkError_IsAFailure()
    {
        var r = await Svc(FakeHttpHandler.Throws(new HttpRequestException("offline"))).ReverseAsync(45, 21);
        Assert.False(r.Success);
        Assert.NotNull(r.Error);
    }

    [Fact] public async Task SearchAndReverse_ShareOneRateLimit()
    {
        var h = new FakeHttpHandler((req, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(req.RequestUri!.AbsolutePath.Contains("reverse") ? "{}" : "[]")
        }));
        var svc = Svc(h, minIntervalMs: 300);

        await Task.WhenAll(svc.SearchAsync("a"), svc.ReverseAsync(45, 21), svc.SearchAddressAsync("b", Timisoara()));

        var times = h.SentAt.Order().ToList();
        Assert.Equal(3, times.Count);
        for (var i = 1; i < times.Count; i++)
            Assert.True(times[i] - times[i - 1] >= TimeSpan.FromMilliseconds(280));
    }
}
