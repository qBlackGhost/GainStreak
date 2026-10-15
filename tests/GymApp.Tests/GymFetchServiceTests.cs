using System.Net;
using System.Text;
using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

public class GymFetchServiceTests : IDisposable
{
    private const string Main = "https://main.test/api/interpreter";
    private const string Mirror = "https://mirror.test/api/interpreter";

    private sealed class Clock(DateTime utc) : TimeProvider
    {
        public DateTime Utc { get; set; } = utc;
        public override DateTimeOffset GetUtcNow() => new(Utc, TimeSpan.Zero);
    }

    private readonly TestDb _db = new();
    private readonly Clock _clock = new(new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc));
    private readonly int _alice;
    private readonly int _timisoara;

    public GymFetchServiceTests()
    {
        _alice = _db.AddUser("alice");
        _timisoara = AddCity(6927733, "Timișoara");
    }

    public void Dispose() => _db.Dispose();

    private int AddCity(long osmId, string name)
    {
        using var db = _db.CreateDbContext();
        var c = new City
        {
            OsmType = OsmElementType.Relation, OsmId = osmId, Name = name, DisplayName = name,
            CenterLatitude = 45.75, CenterLongitude = 21.22,
            South = 45.6838246, West = 21.1116821, North = 45.8341735, East = 21.3272446
        };
        db.Cities.Add(c);
        db.SaveChanges();
        return c.Id;
    }

    private GymFetchService Svc(FakeHttpHandler h, TimeSpan? timeout = null) =>
        new(_db, h, [Main, Mirror], TimeSpan.Zero, timeout, _clock, retryDelay: TimeSpan.Zero);

    private static HttpResponseMessage Resp(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string Elements(params string[] elements) => $$"""{"elements":[{{string.Join(",", elements)}}]}""";
    private static string Node(long id, string name, double lat = 45.75) =>
        $$$"""{"type":"node","id":{{{id}}},"lat":{{{lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}}},"lon":21.22,"tags":{"name":"{{{name}}}"}}""";

    private List<Gym> Gyms(int cityId)
    {
        using var db = _db.CreateDbContext();
        return db.Gyms.AsNoTracking().Where(g => g.CityId == cityId).OrderBy(g => g.Id).ToList();
    }

    private DateTime? FetchedAt(int cityId)
    {
        using var db = _db.CreateDbContext();
        return db.Cities.Single(c => c.Id == cityId).GymsFetchedAt;
    }

    [Fact] public async Task FirstUse_LoadsTimisoaraGyms_AndSetsFetchedAt()
    {
        var h = FakeHttpHandler.Json(Fixtures.Read("overpass_timisoara.json"));
        var r = await Svc(h).RefreshIfNeededAsync(_timisoara);

        Assert.Equal(GymRefreshOutcome.Fetched, r.Outcome);
        Assert.Equal(21, r.Added);
        Assert.Equal(_clock.Utc, r.FetchedAt);
        Assert.Equal(_clock.Utc, FetchedAt(_timisoara));

        var gyms = Gyms(_timisoara);
        Assert.Equal(21, gyms.Count);
        Assert.All(gyms, g => Assert.True(g.IsActive && g.OwnerUserId is null && g.Latitude is not null));
        Assert.Contains(gyms, g => g is { Name: "Smartfit Studio 2", Address: "Calea Buziașului 11" });

        var req = Assert.Single(h.Requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal(Main, req.RequestUri!.AbsoluteUri);
        Assert.Equal(OsmHttp.UserAgent, req.Headers.UserAgent.ToString());
    }

    [Fact] public async Task Request_AsksForTheCitysBox()
    {
        string? body = null;
        var h = new FakeHttpHandler(async (req, _) =>
        {
            body = await req.Content!.ReadAsStringAsync();
            return Resp(Elements());
        });
        await Svc(h).RefreshAsync(_timisoara);
        var query = WebUtility.UrlDecode(body!.Replace("data=", ""));
        Assert.Equal(Overpass.GymQuery(45.6838246, 21.1116821, 45.8341735, 21.3272446), query);
    }

    [Fact] public async Task Refetch_CreatesNoDuplicates_AndMovesFetchedAt()
    {
        var svc = Svc(FakeHttpHandler.Json(Fixtures.Read("overpass_timisoara.json")));
        await svc.RefreshAsync(_timisoara);
        var ids = Gyms(_timisoara).Select(g => g.Id).ToList();

        _clock.Utc = _clock.Utc.AddDays(1);
        var r = await svc.RefreshAsync(_timisoara);

        Assert.Equal(GymRefreshOutcome.Fetched, r.Outcome);
        Assert.False(r.Changed);
        Assert.Equal(ids, Gyms(_timisoara).Select(g => g.Id));
        Assert.Equal(_clock.Utc, FetchedAt(_timisoara));
    }

    [Fact] public async Task FreshCache_IsNotFetchedAgain_UntilItIsOlderThan30Days()
    {
        var h = FakeHttpHandler.Json(Elements(Node(1, "A")));
        var svc = Svc(h);
        await svc.RefreshIfNeededAsync(_timisoara);
        var first = _clock.Utc;

        _clock.Utc = first.AddDays(30);
        var r = await svc.RefreshIfNeededAsync(_timisoara);
        Assert.Equal(GymRefreshOutcome.UpToDate, r.Outcome);
        Assert.Equal(first, r.FetchedAt);
        Assert.Single(h.Requests);

        _clock.Utc = first.AddDays(31);
        Assert.Equal(GymRefreshOutcome.Fetched, (await svc.RefreshIfNeededAsync(_timisoara)).Outcome);
        Assert.Equal(2, h.Requests.Count);
    }

    [Fact] public async Task RefreshButton_FetchesEvenWhenFresh()
    {
        var h = FakeHttpHandler.Json(Elements(Node(1, "A")));
        var svc = Svc(h);
        await svc.RefreshAsync(_timisoara);
        await svc.RefreshAsync(_timisoara);
        Assert.Equal(2, h.Requests.Count);
    }

    [Fact] public async Task UpdatesRenames_DeactivatesMissing_ReactivatesReturning()
    {
        var body = Elements(Node(1, "A"), Node(2, "B"));
        var svc = Svc(new FakeHttpHandler((_, _) => Task.FromResult(Resp(body))));
        await svc.RefreshAsync(_timisoara);

        body = Elements(Node(1, "A renamed"));
        var r = await svc.RefreshAsync(_timisoara);
        Assert.Equal((0, 1, 1), (r.Added, r.Updated, r.Deactivated));
        var gyms = Gyms(_timisoara);
        Assert.Equal(2, gyms.Count);   // never deleted
        Assert.Equal("A renamed", gyms[0].Name);
        Assert.False(gyms[1].IsActive);

        body = Elements(Node(1, "A renamed"), Node(2, "B"));
        r = await svc.RefreshAsync(_timisoara);
        Assert.Equal((0, 1, 0), (r.Added, r.Updated, r.Deactivated));
        Assert.True(Gyms(_timisoara)[1].IsActive);
    }

    [Fact] public async Task CustomGyms_AreNeverTouched()
    {
        int customId;
        using (var db = _db.CreateDbContext())
        {
            var custom = new Gym { CityId = _timisoara, Name = "Garage gym", Address = "Home", OwnerUserId = _alice };
            db.Gyms.Add(custom);
            db.SaveChanges();
            customId = custom.Id;
        }

        var r = await Svc(FakeHttpHandler.Json(Elements())).RefreshAsync(_timisoara);

        Assert.Equal(0, r.Deactivated);
        var g = Gyms(_timisoara).Single(x => x.Id == customId);
        Assert.True(g.IsActive);
        Assert.Equal(("Garage gym", "Home"), (g.Name, g.Address));
        Assert.Null(g.Latitude);
    }

    [Fact] public async Task GymUsedByALog_ThatDisappears_IsDeactivated_AndTheLogKeepsIt()
    {
        var body = Elements(Node(1, "A"));
        var svc = Svc(new FakeHttpHandler((_, _) => Task.FromResult(Resp(body))));
        await svc.RefreshAsync(_timisoara);
        var gymId = Gyms(_timisoara).Single().Id;
        using (var db = _db.CreateDbContext())
        {
            db.WorkoutLogs.Add(new WorkoutLog { UserId = _alice, Date = new DateOnly(2026, 10, 14), GymId = gymId });
            db.SaveChanges();
        }

        body = Elements();
        Assert.Equal(GymRefreshOutcome.Fetched, (await svc.RefreshAsync(_timisoara)).Outcome);

        using var read = _db.CreateDbContext();
        Assert.False(read.Gyms.Single(g => g.Id == gymId).IsActive);
        Assert.Equal(gymId, read.WorkoutLogs.Single().GymId);
    }

    [Fact] public async Task SameOsmGym_InTwoOverlappingCities_GetsARowInEach()
    {
        var dumbravita = AddCity(111, "Dumbrăvița");
        var svc = Svc(FakeHttpHandler.Json(Elements(Node(1, "A"))));
        await svc.RefreshAsync(_timisoara);
        await svc.RefreshAsync(dumbravita);

        Assert.Single(Gyms(_timisoara));
        Assert.Single(Gyms(dumbravita));

        // and a refetch of one city leaves the other city's row alone
        var r = await Svc(FakeHttpHandler.Json(Elements())).RefreshAsync(dumbravita);
        Assert.Equal(1, r.Deactivated);
        Assert.True(Gyms(_timisoara).Single().IsActive);
    }

    [Fact] public async Task Failure_WithoutCache_IsFailed_AndFetchedAtStaysEmpty()
    {
        var r = await Svc(FakeHttpHandler.Throws(new HttpRequestException("offline"))).RefreshIfNeededAsync(_timisoara);

        Assert.Equal(GymRefreshOutcome.Failed, r.Outcome);
        Assert.Contains("internet", r.Error);
        Assert.Null(r.FetchedAt);
        Assert.Null(FetchedAt(_timisoara));
        Assert.Empty(Gyms(_timisoara));
    }

    [Fact] public async Task Failure_WithCache_KeepsTheCachedGyms_AndTheOldDate()
    {
        await Svc(FakeHttpHandler.Json(Elements(Node(1, "A"), Node(2, "B")))).RefreshAsync(_timisoara);
        var first = _clock.Utc;
        _clock.Utc = first.AddDays(40);

        var r = await Svc(FakeHttpHandler.Json("", HttpStatusCode.ServiceUnavailable)).RefreshIfNeededAsync(_timisoara);

        Assert.Equal(GymRefreshOutcome.FailedWithCache, r.Outcome);
        Assert.Equal(first, r.FetchedAt);
        Assert.Equal(first, FetchedAt(_timisoara));
        Assert.All(Gyms(_timisoara), g => Assert.True(g.IsActive));
    }

    [Fact] public async Task RemarkFromOverpass_IsAFailure_AndDeactivatesNothing()
    {
        await Svc(FakeHttpHandler.Json(Elements(Node(1, "A"), Node(2, "B")))).RefreshAsync(_timisoara);

        const string cutShort = """{"elements":[],"remark":"runtime error: Query timed out after 26 seconds."}""";
        var r = await Svc(FakeHttpHandler.Json(cutShort)).RefreshAsync(_timisoara);

        Assert.Equal(GymRefreshOutcome.FailedWithCache, r.Outcome);
        Assert.All(Gyms(_timisoara), g => Assert.True(g.IsActive));
    }

    [Fact] public async Task ServerError_OnTheMainServer_FallsBackToTheMirror()
    {
        var h = new FakeHttpHandler((req, _) => Task.FromResult(req.RequestUri!.AbsoluteUri == Main
            ? Resp("<html>502</html>", HttpStatusCode.BadGateway)
            : Resp(Elements(Node(1, "A")))));

        var r = await Svc(h).RefreshAsync(_timisoara);

        Assert.Equal(GymRefreshOutcome.Fetched, r.Outcome);
        Assert.Equal([Main, Mirror], h.Requests.Select(x => x.RequestUri!.AbsoluteUri));
    }

    [Fact] public async Task BothServersDown_TriesEachTwice_ThenGivesTheLastError()
    {
        var h = FakeHttpHandler.Json("", HttpStatusCode.GatewayTimeout);
        var r = await Svc(h).RefreshAsync(_timisoara);
        Assert.Equal(GymRefreshOutcome.Failed, r.Outcome);
        Assert.Contains("HTTP 504", r.Error);
        Assert.Equal([Main, Mirror, Main, Mirror], h.Requests.Select(x => x.RequestUri!.AbsoluteUri));
    }

    [Fact] public async Task BothServersBusyForAMoment_TheSecondRoundSucceeds()
    {
        var calls = 0;
        var h = new FakeHttpHandler((_, _) => Task.FromResult(++calls <= 2
            ? Resp("", HttpStatusCode.TooManyRequests)
            : Resp(Elements(Node(1, "A")))));

        var r = await Svc(h).RefreshAsync(_timisoara);

        Assert.Equal(GymRefreshOutcome.Fetched, r.Outcome);
        Assert.Equal(3, h.Requests.Count);
        Assert.Single(Gyms(_timisoara));
    }

    [Fact] public async Task NoConnection_IsRetried_ThenReported()
    {
        var h = FakeHttpHandler.Throws(new HttpRequestException("offline"));
        var r = await Svc(h).RefreshAsync(_timisoara);
        Assert.Contains("internet", r.Error);
        Assert.Equal(4, h.Requests.Count);
    }

    [Fact] public async Task WaitsBetweenRounds()
    {
        var h = FakeHttpHandler.Json("", HttpStatusCode.ServiceUnavailable);
        var svc = new GymFetchService(_db, h, [Main], TimeSpan.Zero, null, _clock, retryDelay: TimeSpan.FromMilliseconds(300));

        await svc.RefreshAsync(_timisoara);

        Assert.Equal(2, h.SentAt.Count);
        Assert.True(h.SentAt[1] - h.SentAt[0] >= TimeSpan.FromMilliseconds(280));
    }

    [Fact] public async Task BadRequest_DoesNotTryTheMirror()
    {
        var h = FakeHttpHandler.Json("", HttpStatusCode.BadRequest);
        Assert.Equal(GymRefreshOutcome.Failed, (await Svc(h).RefreshAsync(_timisoara)).Outcome);
        Assert.Single(h.Requests);
    }

    [Fact] public async Task Timeout_IsAFailure_WithoutWaitingOnTheMirror()
    {
        var h = new FakeHttpHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Resp("");
        });
        var r = await Svc(h, timeout: TimeSpan.FromMilliseconds(100)).RefreshAsync(_timisoara);

        Assert.Equal(GymRefreshOutcome.Failed, r.Outcome);
        Assert.Contains("too long", r.Error);
        Assert.Single(h.Requests);
    }

    [Fact] public async Task TwoPickersOpeningAtOnce_FetchOnlyOnce()
    {
        var h = new FakeHttpHandler(async (_, _) =>
        {
            await Task.Delay(100);
            return Resp(Elements(Node(1, "A")));
        });
        var svc = Svc(h);

        var results = await Task.WhenAll(svc.RefreshIfNeededAsync(_timisoara), svc.RefreshIfNeededAsync(_timisoara));

        Assert.Single(h.Requests);
        Assert.Contains(results, r => r.Outcome == GymRefreshOutcome.Fetched);
        Assert.Contains(results, r => r.Outcome == GymRefreshOutcome.UpToDate);
        Assert.Single(Gyms(_timisoara));
    }

    [Fact] public async Task UnknownCity_Fails_WithoutARequest()
    {
        var h = FakeHttpHandler.Json(Elements());
        Assert.Equal(GymRefreshOutcome.Failed, (await Svc(h).RefreshAsync(999)).Outcome);
        Assert.Empty(h.Requests);
    }
}
