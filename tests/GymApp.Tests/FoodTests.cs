using System.Net;
using System.Text;
using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

public class FoodRulesTests
{
    [Fact] public void Per100_AcceptsUsualNumbers_AndMissingMacros()
    {
        Assert.Null(FoodRules.CheckPer100(539, 6.3, 57.5, 30.9));
        Assert.Null(FoodRules.CheckPer100(0, null, null, null));
        Assert.Null(FoodRules.CheckPer100(884, 0, 0, 100));
    }

    [Fact] public void Per100_RejectsOutOfRange_AndMacrosThatAddUpToMoreThanTheFoodWeighs()
    {
        Assert.Equal(FoodRules.LooksWrong, FoodRules.CheckPer100(901, 1, 1, 1));
        Assert.Equal(FoodRules.LooksWrong, FoodRules.CheckPer100(-1, 1, 1, 1));
        Assert.Equal(FoodRules.LooksWrong, FoodRules.CheckPer100(100, 101, 0, 0));
        Assert.Equal(FoodRules.LooksWrong, FoodRules.CheckPer100(500, 40, 40, 23));   // 103 g in 100 g
        Assert.Null(FoodRules.CheckPer100(500, 40, 40, 22));                          // 102: rounding slack
        Assert.Equal(FoodRules.LooksWrong, FoodRules.CheckPer100(double.NaN, 1, 1, 1));
    }

    [Fact] public void NoEnergy_CantBeLoggedAsIs()
        => Assert.Equal(FoodRules.NoEnergy, FoodRules.CheckPer100(null, 5, 5, 5));

    [Fact] public void Serving_MustBeBetween1gAnd2000g()
    {
        Assert.Null(FoodRules.CheckServing(null));
        Assert.Null(FoodRules.CheckServing(1));
        Assert.Null(FoodRules.CheckServing(2000));
        Assert.NotNull(FoodRules.CheckServing(0.5));
        Assert.NotNull(FoodRules.CheckServing(2001));
    }

    [Fact] public void KjToKcal_UsesTheThermochemicalCalorie() => Assert.Equal(400, FoodRules.KjToKcal(1673.6), 0);

    [Fact] public void Amounts_RoundHalfUp()
    {
        Assert.Equal(75, FoodRules.KcalFor(100, 75));
        Assert.Equal(7, FoodRules.KcalFor(46.5, 15));   // 6.975
        Assert.Equal(6.8, FoodRules.Scale(9.01, 75));   // 6.7575
        Assert.Equal(50, FoodRules.To100(15, 30));
    }

    [Fact] public void Input_HasSpecificMessages()
    {
        Assert.Equal("Give the food a name.", FoodRules.CheckInput(" ", null, 100, null, null, null, null, null));
        Assert.Equal("Enter the calories.", FoodRules.CheckInput("Oats", null, null, null, null, null, null, null));
        Assert.Contains("between 0 and 900", FoodRules.CheckInput("Oats", null, 950, null, null, null, null, null));
        Assert.Contains("add up", FoodRules.CheckInput("Oats", null, 400, 60, 60, 0, null, null));
        Assert.Null(FoodRules.CheckInput("Oats", "Quaker", 370, 13, 60, 7, 40, "1 bowl"));
    }
}

public class FoodCatalogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gymapp-catalog-{Guid.NewGuid():N}");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact] public async Task Extracts_OnFirstUse_AndAgainOnlyWhenTheVersionChanges()
    {
        var v1 = TestCatalog.Build();
        var catalog = new FoodCatalog(_folder, () => TestCatalog.Open(v1), () => TestCatalog.Text("v1"));
        Assert.False(File.Exists(catalog.Path));

        Assert.True(catalog.EnsureExtracted());
        Assert.Equal("v1", catalog.Version);
        Assert.Equal(2, (await catalog.SearchAsync("chicken")).Count);
    }

    [Fact] public async Task NewVersion_ReplacesTheFile_SameVersionKeepsIt()
    {
        var v1 = TestCatalog.Build();
        new FoodCatalog(_folder, () => TestCatalog.Open(v1), () => TestCatalog.Text("v1")).EnsureExtracted();
        var firstWrite = File.GetLastWriteTimeUtc(Path.Combine(_folder, FoodCatalog.FileName));
        await Task.Delay(30);

        // same version: not copied again, even though the embedded file now differs
        var v1b = TestCatalog.Build(extra: true);
        var same = new FoodCatalog(_folder, () => TestCatalog.Open(v1b), () => TestCatalog.Text("v1"));
        Assert.True(same.EnsureExtracted());
        Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(same.Path));
        Assert.Empty(await same.SearchAsync("Quinoa"));

        // new version: extracted again
        var v2 = new FoodCatalog(_folder, () => TestCatalog.Open(v1b), () => TestCatalog.Text("v2"));
        Assert.True(v2.EnsureExtracted());
        Assert.Equal("v2", v2.Version);
        Assert.Single(await v2.SearchAsync("Quinoa"));
        Assert.Equal("v2", File.ReadAllText(Path.Combine(_folder, "foods.version")));
    }

    [Fact] public async Task MissingCatalog_FindsNothingInsteadOfThrowing()
    {
        var catalog = new FoodCatalog(_folder, () => null, () => null);
        Assert.False(catalog.EnsureExtracted());
        Assert.Empty(await catalog.SearchAsync("chicken"));
        Assert.Null(await catalog.GetAsync(1001));
    }

    [Fact] public async Task PrefixSearch_MatchesEveryWord_BestFirst()
    {
        var path = TestCatalog.Build();
        var catalog = new FoodCatalog(_folder, () => TestCatalog.Open(path), () => TestCatalog.Text("v1"));

        var found = await catalog.SearchAsync("chick bre");
        Assert.Equal(1001, Assert.Single(found).FdcId);
        Assert.Equal("1 breast", found[0].ServingName);
        Assert.Equal(172, found[0].ServingGrams);

        Assert.Equal(2, (await catalog.SearchAsync("chick")).Count);
        Assert.Empty(await catalog.SearchAsync("chick pizza"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"*()-")]
    public async Task PunctuationAndBlankText_NeverBreakTheQuerySyntax(string text)
    {
        var path = TestCatalog.Build();
        var catalog = new FoodCatalog(_folder, () => TestCatalog.Open(path), () => TestCatalog.Text("v1"));
        Assert.Empty(await catalog.SearchAsync(text));
        Assert.Empty(await catalog.SearchAsync("chick\" OR \"x"));   // a quote is just a separator
    }

    [Fact] public async Task WholeWordsComeBeforePrefixMatches()
    {
        var path = TestCatalog.Build(extra: true);
        var catalog = new FoodCatalog(_folder, () => TestCatalog.Open(path), () => TestCatalog.Text("v1"));
        var found = await catalog.SearchAsync("egg");
        Assert.Equal(["Egg, whole, raw, fresh", "Eggnog"], found.Select(f => f.Name));   // the shorter "Eggnog" doesn't win
        Assert.Equal(["Eggnog"], (await catalog.SearchAsync("eggn")).Select(f => f.Name));
        Assert.Single(await catalog.SearchAsync("egg", limit: 1));
    }

    [Fact] public void BuildMatch_QuotesEachWordAsAPrefix()
    {
        Assert.Equal("\"chick\"* \"bre\"*", FoodCatalog.BuildMatch("  chick,  bre "));
        Assert.Equal("\"chick\" \"bre\"", FoodCatalog.BuildMatch("chick bre", prefix: false));
        Assert.Equal("\"crème\"*", FoodCatalog.BuildMatch("crème"));
        Assert.Null(FoodCatalog.BuildMatch("+-*"));
    }

    [Fact] public async Task Get_ReturnsPortions_InOrder()
    {
        var path = TestCatalog.Build();
        var catalog = new FoodCatalog(_folder, () => TestCatalog.Open(path), () => TestCatalog.Text("v1"));
        var egg = (await catalog.GetAsync(1005))!;
        Assert.Equal("Egg, whole, raw, fresh", egg.Name);
        Assert.Equal(["1 large", "1 cup"], egg.Portions.Select(p => p.Name));
        Assert.Equal(50, egg.Portions[0].Grams);
    }
}

public class OpenFoodFactsClientTests
{
    private static OpenFoodFactsClient Client(FakeHttpHandler h, int ms = 0, TimeSpan? timeout = null) =>
        new(h, TimeSpan.FromMilliseconds(ms), TimeSpan.FromMilliseconds(ms), timeout);

    [Fact] public async Task Lookup_MapsAProduct_AndSendsTheAppUserAgent()
    {
        var h = FakeHttpHandler.Json(Fixtures.Read("off_product_nutella.json"));
        var r = await Client(h).LookupAsync(" 3017620422003 ", "ro");

        Assert.Equal(LookupStatus.Found, r.Status);
        var f = r.Food!;
        Assert.Equal(FoodSource.OpenFoodFacts, f.Source);
        Assert.Equal("3017620422003", f.SourceKey);
        Assert.Equal("Nutella", f.Name);
        Assert.Equal("Ferrero", f.Brand);   // the first of "Ferrero, Nutella"
        Assert.Equal(539, f.KcalPer100);
        Assert.Equal(6.3, f.ProteinPer100);
        Assert.Equal(57.5, f.CarbsPer100);
        Assert.Equal(30.9, f.FatPer100);
        Assert.Null(f.ServingGrams);
        Assert.False(f.IsLiquid);
        Assert.Null(f.Problem);

        var req = Assert.Single(h.Requests);
        var url = req.RequestUri!.AbsoluteUri;
        Assert.StartsWith("https://world.openfoodfacts.org/api/v2/product/3017620422003?fields=", url);
        Assert.Contains("product_name_ro", url);   // the phone's language first
        Assert.Contains("nutriments", url);
        var ua = req.Headers.UserAgent.ToString();
        Assert.Contains("GainStreak/", ua);
        Assert.Contains(AppHttp.Site, ua);
        Assert.DoesNotContain("@", ua);   // never an email address
    }

    [Fact] public async Task Lookup_ReadsTheServing_AndLiquids()
    {
        var biscuits = (await Client(FakeHttpHandler.Json(Fixtures.Read("off_product_biscuits.json"))).LookupAsync("8000500310427")).Food!;
        Assert.Equal(13.8, biscuits.ServingGrams);
        Assert.Equal("1 portion (13.8 g)", biscuits.ServingName);
        Assert.Equal(71, biscuits.Kcal);   // one serving

        var cola = (await Client(FakeHttpHandler.Json(Fixtures.Read("off_product_cola.json"))).LookupAsync("5449000000996", "ro")).Food!;
        Assert.True(cola.IsLiquid);
        Assert.Equal(330, cola.ServingGrams);   // serving_quantity as a string
        Assert.Equal("Coca-Cola", cola.Name);   // product_name_ro first
    }

    [Fact] public async Task KjOnly_BecomesKcal_AndNumbersMayBeStrings()
    {
        var f = (await Client(FakeHttpHandler.Json(Fixtures.Read("off_product_kj_only.json"))).LookupAsync("5941000000017")).Food!;
        Assert.Equal(400.1, f.KcalPer100);    // 1674 kJ / 4.184, one decimal
        Assert.Equal(60.5, f.CarbsPer100);    // "60,5"
        Assert.Equal(3.2, f.FatPer100);
        Assert.Equal("Test kJ only", f.Name);   // no ro name asked for: product_name
        Assert.Null(f.Problem);
    }

    [Fact] public async Task NoEnergy_AndNumbersThatLookWrong_ComeBackWithAProblem()
    {
        var none = (await Client(FakeHttpHandler.Json(Fixtures.Read("off_product_no_energy.json"))).LookupAsync("5941487002932")).Food!;
        Assert.Equal(FoodRules.NoEnergy, none.Problem);
        Assert.True(none.NeedsEdit);
        Assert.Equal(400, none.ServingGrams);   // "400g"

        var wrong = (await Client(FakeHttpHandler.Json(Fixtures.Read("off_product_wrong.json"))).LookupAsync("5941000000024")).Food!;
        Assert.Equal(FoodRules.LooksWrong, wrong.Problem);
    }

    [Fact] public async Task UnknownBarcode_IsNotFound_WhetherTheStatusIs0Or404()
    {
        Assert.Equal(LookupStatus.NotFound, (await Client(FakeHttpHandler.Json(Fixtures.Read("off_not_found.json"))).LookupAsync("5941000010000")).Status);
        Assert.Equal(LookupStatus.NotFound, (await Client(FakeHttpHandler.Json("{}", HttpStatusCode.NotFound)).LookupAsync("5941000010000")).Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("59414870029AB")]
    [InlineData("123456789012345")]
    public async Task NotABarcode_IsRefusedWithoutARequest(string text)
    {
        var h = FakeHttpHandler.Json("{}");
        Assert.Equal(LookupStatus.NotFound, (await Client(h).LookupAsync(text)).Status);
        Assert.Empty(h.Requests);
    }

    [Fact] public async Task Offline_AndTimeout_AreReportedAsOffline_NeverThrown()
    {
        Assert.Equal(LookupStatus.Offline, (await Client(FakeHttpHandler.Throws(new HttpRequestException("no network"))).LookupAsync("3017620422003")).Status);

        var slow = new FakeHttpHandler(async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(5), ct); return new HttpResponseMessage(); });
        var r = await Client(slow, timeout: TimeSpan.FromMilliseconds(100)).LookupAsync("3017620422003");
        Assert.Equal(LookupStatus.Offline, r.Status);   // the 10-second rule, shortened for the test
    }

    [Fact] public async Task ServerTrouble_IsFailed_NotOffline()
    {
        var busy = (await Client(FakeHttpHandler.Json("{}", HttpStatusCode.TooManyRequests)).LookupAsync("3017620422003"));
        Assert.Equal(LookupStatus.Failed, busy.Status);
        Assert.Contains("busy", busy.Error);
        Assert.Equal(LookupStatus.Failed, (await Client(FakeHttpHandler.Json("not json")).LookupAsync("3017620422003")).Status);
    }

    [Fact] public async Task Search_MapsHits_SkipsOnesWithNoName_AndSendsTheLanguage()
    {
        var h = FakeHttpHandler.Json(Fixtures.Read("off_search_covrigi.json"));
        var r = await Client(h).SearchAsync("  covrigi ", "ro");

        Assert.True(r.Success);
        Assert.Equal(["Covrigi de buzau", "Bretzel", "covrigi din faina dietetica-integrala de grau"], r.Foods.Select(f => f.Name));
        Assert.Equal("Boromir", r.Foods[0].Brand);   // a list in the search API
        Assert.Equal(FoodRules.NoEnergy, r.Foods[0].Problem);
        Assert.Equal(393, r.Foods[1].KcalPer100);

        var url = Assert.Single(h.Requests).RequestUri!.AbsoluteUri;
        Assert.StartsWith("https://search.openfoodfacts.org/search?q=covrigi&", url);
        Assert.Contains("langs=ro", url);
    }

    [Fact] public async Task ShortSearch_MakesNoRequest()
    {
        var h = FakeHttpHandler.Json("{}");
        var r = await Client(h).SearchAsync("co");
        Assert.True(r.Success);
        Assert.Empty(r.Foods);
        Assert.Empty(h.Requests);
    }

    [Fact] public async Task Search_Offline_SaysSo()
    {
        var r = await Client(FakeHttpHandler.Throws(new HttpRequestException("no network"))).SearchAsync("covrigi");
        Assert.False(r.Success);
        Assert.True(r.Offline);
    }

    [Fact] public async Task EachEndpointHasItsOwnThrottle()
    {
        var h = FakeHttpHandler.Json(Fixtures.Read("off_product_nutella.json"));
        var c = new OpenFoodFactsClient(h, TimeSpan.FromMilliseconds(150), TimeSpan.Zero);
        await c.LookupAsync("3017620422003");
        await c.LookupAsync("3017620422003");
        Assert.True((h.SentAt[1] - h.SentAt[0]).TotalMilliseconds >= 140);   // product reads are spaced
    }

    [Fact] public void UserAgentWithSite_KeepsTheAppNameVersionAndPlatform()
    {
        Assert.Equal("GainStreak/0.26.0 (iOS; student project; https://gainstreak.app)",
            AppHttp.WithSite(AppHttp.ForPlatform("0.26.0", "iOS")));
        Assert.Equal("GainStreak/1.0 (https://gainstreak.app)", AppHttp.WithSite("GainStreak/1.0"));
    }
}

public class FoodServiceTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 21);
    private static readonly DateOnly Yesterday = Today.AddDays(-1);

    private readonly TestDb _db = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gymapp-food-{Guid.NewGuid():N}");
    private readonly string _catalogFile = TestCatalog.Build();
    private readonly CalorieService _calories;
    private readonly PointsService _points;
    private readonly int _alice;
    private readonly int _bob;
    private FakeHttpHandler _http = FakeHttpHandler.Json("{\"hits\":[]}");

    public FoodServiceTests()
    {
        _points = new PointsService(_db);
        _calories = new CalorieService(_db, _points);
        _alice = _db.AddUser("alice");
        _bob = _db.AddUser("bob");
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        File.Delete(_catalogFile);
    }

    private FoodService Svc(FakeHttpHandler? http = null) =>
        new(_db, new FoodCatalog(_folder, () => TestCatalog.Open(_catalogFile), () => TestCatalog.Text("t1")),
            new OpenFoodFactsClient(http ?? _http, TimeSpan.Zero, TimeSpan.Zero), _calories, _points);

    private FakeHttpHandler Off(params (string Url, string Json)[] routes) =>
        new((req, _) =>
        {
            var url = req.RequestUri!.AbsoluteUri;
            var hit = routes.FirstOrDefault(r => url.Contains(r.Url));
            return Task.FromResult(hit.Url is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(hit.Json, Encoding.UTF8, "application/json") });
        });

    private async Task<FoodInfo> Catalog(string text, long? fdcId = null)
    {
        var found = await Svc().SearchLocalAsync(_alice, text);
        return found.First(f => f.Source == FoodSource.Usda && (fdcId is null || f.SourceKey == fdcId.ToString()));
    }

    private async Task<CalorieLog?> LogRow(DateOnly? day = null)
    {
        await using var db = _db.CreateDbContext();
        return await db.CalorieLogs.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == _alice && c.Date == (day ?? Today));
    }

    private void SetGoal(int kcal)
    {
        using var db = _db.CreateDbContext();
        db.Users.Single(u => u.Id == _alice).DailyCalorieGoal = kcal;
        db.SaveChanges();
    }

    // ------------------------------------------------------------ the roll-up

    [Fact] public async Task Add_LogsTheFood_AndSetsTheDaysTotal()
    {
        var egg = await Catalog("egg", 1005);   // 143 kcal per 100 g, "1 large" = 50 g
        var r = await Svc().LogAsync(_alice, egg, Meal.Breakfast, 2, AmountUnit.Servings, Today);

        Assert.True(r.Success, r.Error);
        Assert.Equal(143, r.Entry!.Kcal);   // 100 g
        Assert.Equal(100, r.Entry.Grams);
        Assert.Equal(2, r.Entry.Servings);
        Assert.Equal(12.6, r.Entry.Protein);   // 12.56 per 100 g
        Assert.Equal(0.7, r.Entry.Carbs);
        Assert.Equal(9.5, r.Entry.Fat);
        Assert.NotNull(r.Points);
        Assert.Equal(143, (await LogRow())!.CaloriesConsumed);

        var day = await Svc().GetDayAsync(_alice, Today);
        Assert.Equal(143, day.Kcal);
        Assert.False(day.TotalOnly);
        Assert.Equal(143, day.KcalIn(Meal.Breakfast));
    }

    [Fact] public async Task TheTotalIsTheSumOfEveryEntry_AcrossMeals()
    {
        var svc = Svc();
        await svc.LogAsync(_alice, await Catalog("egg", 1005), Meal.Breakfast, 100, AmountUnit.Grams, Today);   // 143
        await svc.LogAsync(_alice, await Catalog("chick bre"), Meal.Lunch, 200, AmountUnit.Grams, Today);       // 330
        await svc.QuickAddAsync(_alice, 450, "Dinner out", Meal.Dinner, Today);

        Assert.Equal(143 + 330 + 450, (await LogRow())!.CaloriesConsumed);
        var day = await svc.GetDayAsync(_alice, Today);
        Assert.Equal([Meal.Breakfast, Meal.Lunch, Meal.Dinner], day.Entries.Select(e => e.Meal));
        Assert.Equal(74.6, day.Protein, 1);   // 12.6 (egg) + 62 (200 g chicken) + nothing for the quick add
    }

    [Fact] public async Task Edit_RecomputesTheTotal_AndTheMeal()
    {
        var svc = Svc();
        var entry = (await svc.LogAsync(_alice, await Catalog("chick bre"), Meal.Lunch, 100, AmountUnit.Grams, Today)).Entry!;
        Assert.Equal(165, (await LogRow())!.CaloriesConsumed);

        var r = await svc.EditEntryAsync(_alice, entry.Id, Meal.Dinner, 2, AmountUnit.Servings, Today);   // 2 x 172 g
        Assert.True(r.Success, r.Error);
        Assert.Equal(568, r.Entry!.Kcal);
        Assert.Equal(Meal.Dinner, r.Entry.Meal);
        Assert.Equal(344, r.Entry.Grams);
        Assert.Equal(568, (await LogRow())!.CaloriesConsumed);
    }

    [Fact] public async Task Delete_RecomputesTheTotal()
    {
        var svc = Svc();
        var a = (await svc.QuickAddAsync(_alice, 300, null, Meal.Snacks, Today)).Entry!;
        await svc.QuickAddAsync(_alice, 200, null, Meal.Snacks, Today);
        Assert.Equal(500, (await LogRow())!.CaloriesConsumed);

        Assert.True((await svc.DeleteEntryAsync(_alice, a.Id, Today)).Success);
        Assert.Equal(200, (await LogRow())!.CaloriesConsumed);
    }

    [Fact] public async Task DeletingTheLastEntry_RemovesTheLog_WhenTheDayWasNotLoggedBefore()
    {
        var svc = Svc();
        var e = (await svc.QuickAddAsync(_alice, 300, null, Meal.Snacks, Today)).Entry!;
        Assert.NotNull(await LogRow());

        var r = await svc.DeleteEntryAsync(_alice, e.Id, Today);
        Assert.True(r.Success);
        Assert.Null(await LogRow());   // the day reads as not logged
        Assert.False((await svc.GetDayAsync(_alice, Today)).TotalOnly);
    }

    [Fact] public async Task ADayLoggedAsATotal_KeepsALogOf0_WhenItsEntriesAreAllDeleted()
    {
        await _calories.LogTodayAsync(_alice, 1800, Today);   // the old one-number form
        var svc = Svc();
        Assert.True((await svc.GetDayAsync(_alice, Today)).TotalOnly);   // "Logged as a total"

        var e = (await svc.QuickAddAsync(_alice, 250, null, Meal.Snacks, Today)).Entry!;
        Assert.Equal(250, (await LogRow())!.CaloriesConsumed);   // the entries replace the typed number
        Assert.True((await LogRow())!.LoggedAsTotal);

        Assert.True((await svc.DeleteEntryAsync(_alice, e.Id, Today)).Success);
        var log = await LogRow();
        Assert.NotNull(log);
        Assert.Equal(0, log!.CaloriesConsumed);
    }

    [Fact] public async Task ADayLoggedAsATotal_ShowsTheTotalWithNoEntries()
    {
        await _calories.LogTodayAsync(_alice, 1800, Yesterday);
        var day = await Svc().GetDayAsync(_alice, Yesterday);
        Assert.True(day.TotalOnly);
        Assert.Equal(1800, day.Kcal);
        Assert.Empty(day.Entries);
    }

    [Fact] public async Task TheGoalIsSnapshotted_LikeLogTodayDoes()
    {
        SetGoal(2000);
        await Svc().QuickAddAsync(_alice, 1900, null, Meal.Lunch, Today);
        var log = (await LogRow())!;
        Assert.Equal(2000, log.GoalAtTime);
        Assert.NotNull(log.GoalTypeAtTime);
    }

    [Fact] public async Task Points_FollowTheLeewayEdge()
    {
        SetGoal(2000);   // within 300 kcal counts: 1,700 to 2,300
        var svc = Svc();
        var e = (await svc.QuickAddAsync(_alice, 1699, null, Meal.Lunch, Today)).Entry!;
        var before = (await _points.GetAsync(_alice))!.Total;
        Assert.False((await _calories.GetAsync(_alice, Today))!.Today!.Hit);

        var r = await svc.EditQuickAddAsync(_alice, e.Id, Meal.Lunch, 1700, null, Today);
        Assert.True(r.Success);
        Assert.True((await _calories.GetAsync(_alice, Today))!.Today!.Hit);
        Assert.Equal(before + 10, (await _points.GetAsync(_alice))!.Total);

        await svc.EditQuickAddAsync(_alice, e.Id, Meal.Lunch, 2300, null, Today);
        Assert.Equal(before + 10, (await _points.GetAsync(_alice))!.Total);   // still inside
        await svc.EditQuickAddAsync(_alice, e.Id, Meal.Lunch, 2301, null, Today);
        Assert.Equal(before, (await _points.GetAsync(_alice))!.Total);        // just outside: the points go again
    }

    [Fact] public async Task Points_AreTakenBack_WhenTheEntryThatEarnedThemIsDeleted()
    {
        SetGoal(2000);
        var svc = Svc();
        var before = (await _points.GetAsync(_alice))!.Total;
        var e = (await svc.QuickAddAsync(_alice, 2000, null, Meal.Lunch, Today)).Entry!;
        Assert.Equal(before + 10, (await _points.GetAsync(_alice))!.Total);

        await svc.DeleteEntryAsync(_alice, e.Id, Today);
        Assert.Equal(before, (await _points.GetAsync(_alice))!.Total);
    }

    // ------------------------------------------------------------ today only, limits

    [Fact] public async Task EntriesCanBeChangedForTodayOnly()
    {
        var svc = Svc();
        var e = (await svc.QuickAddAsync(_alice, 400, "Pasta", Meal.Dinner, Yesterday)).Entry!;

        Assert.Equal(FoodService.TodayOnly, (await svc.EditQuickAddAsync(_alice, e.Id, Meal.Dinner, 500, null, Today)).Error);
        Assert.Equal(FoodService.TodayOnly, (await svc.DeleteEntryAsync(_alice, e.Id, Today)).Error);
        Assert.Equal(400, (await LogRow(Yesterday))!.CaloriesConsumed);
        Assert.Single((await svc.GetDayAsync(_alice, Yesterday)).Entries);   // past days still show their entries
    }

    [Fact] public async Task AnotherUsersEntry_CantBeTouched()
    {
        var svc = Svc();
        var e = (await svc.QuickAddAsync(_alice, 400, null, Meal.Dinner, Today)).Entry!;
        Assert.False((await svc.DeleteEntryAsync(_bob, e.Id, Today)).Success);
        Assert.False((await svc.EditQuickAddAsync(_bob, e.Id, Meal.Dinner, 1, null, Today)).Success);
        Assert.Empty((await svc.GetDayAsync(_bob, Today)).Entries);
    }

    [Fact] public async Task ADayCantGoOverTheLimit()
    {
        var svc = Svc();
        Assert.False((await svc.QuickAddAsync(_alice, CalorieGoals.MaxConsumed + 1, null, Meal.Snacks, Today)).Success);
        Assert.True((await svc.QuickAddAsync(_alice, CalorieGoals.MaxConsumed, null, Meal.Snacks, Today)).Success);
        var more = await svc.QuickAddAsync(_alice, 1, null, Meal.Snacks, Today);
        Assert.False(more.Success);
        Assert.Contains("15,000", more.Error);
        Assert.Single((await svc.GetDayAsync(_alice, Today)).Entries);   // the refused one wasn't saved
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(5001)]
    public async Task AmountsMustBeSensible(double grams)
    {
        var r = await Svc().LogAsync(_alice, await Catalog("egg", 1005), Meal.Breakfast, grams, AmountUnit.Grams, Today);
        Assert.False(r.Success);
        Assert.Null(await LogRow());
    }

    [Fact] public async Task ServingsNeedAServing()
    {
        var apple = await Catalog("apple");   // no serving in the catalog
        Assert.False(apple.HasServing);
        var r = await Svc().LogAsync(_alice, apple, Meal.Snacks, 1, AmountUnit.Servings, Today);
        Assert.False(r.Success);
        Assert.Contains("grams", r.Error);
    }

    // ------------------------------------------------------------ snapshots, own foods

    [Fact] public async Task PastDays_KeepTheirNumbers_WhenTheFoodIsEditedLater()
    {
        var svc = Svc();
        var mine = (await svc.CreateFoodAsync(_alice, new FoodInput("Protein bar", "Acme", false, 400, 30, 40, 10, "1 bar", 50))).Food!;
        await svc.LogAsync(_alice, mine, Meal.Snacks, 1, AmountUnit.Servings, Yesterday);
        Assert.Equal(200, (await svc.GetDayAsync(_alice, Yesterday)).Kcal);

        var edited = await svc.SaveFoodEditAsync(_alice, mine, new FoodInput("Protein bar", "Acme", false, 500, 30, 40, 10, "1 bar", 50));
        Assert.Equal(mine.FoodId, edited.Food!.FoodId);   // a "Mine" food is changed in place

        var day = await svc.GetDayAsync(_alice, Yesterday);
        Assert.Equal(200, day.Kcal);                      // the snapshot
        Assert.Equal(200, (await LogRow(Yesterday))!.CaloriesConsumed);
        var today = await svc.LogAsync(_alice, edited.Food, Meal.Snacks, 1, AmountUnit.Servings, Today);
        Assert.Equal(250, today.Entry!.Kcal);             // new entries use the new numbers
    }

    [Fact] public async Task EditingACatalogFood_MakesAMineCopy_AndTheOriginalStays()
    {
        var svc = Svc();
        var chicken = await Catalog("chick bre");
        await svc.LogAsync(_alice, chicken, Meal.Lunch, 100, AmountUnit.Grams, Today);

        var copy = (await svc.SaveFoodEditAsync(_alice, chicken, new FoodInput("Chicken breast (mine)", null, false, 150, 30, 0, 3, null, null))).Food!;
        Assert.Equal(FoodSource.Mine, copy.Source);
        Assert.NotEqual(0, copy.FoodId);

        await using var db = _db.CreateDbContext();
        var rows = await db.Foods.Where(f => f.UserId == _alice).ToListAsync();
        Assert.Contains(rows, f => f.Source == FoodSource.Usda && f.SourceKey == "1001" && f.KcalPer100 == 165);   // untouched
        Assert.Contains(rows, f => f.Source == FoodSource.Mine && f.KcalPer100 == 150);
    }

    [Fact] public async Task ANewFood_IsValidated_AndOwnedByItsUser()
    {
        var svc = Svc();
        Assert.False((await svc.CreateFoodAsync(_alice, new FoodInput("", null, false, 100, null, null, null, null, null))).Success);
        Assert.False((await svc.CreateFoodAsync(_alice, new FoodInput("Oats", null, false, 1000, null, null, null, null, null))).Success);
        Assert.False((await svc.CreateFoodAsync(_alice, new FoodInput("Oats", null, false, 100, null, null, null, "1 bowl", null))).Success);   // serving name, no grams
        Assert.False((await svc.CreateFoodAsync(_alice, new FoodInput("Oats", null, false, 100, null, null, null, null, null, "abc"))).Success);

        var ok = await svc.CreateFoodAsync(_alice, new FoodInput(" Oats ", " ", false, 370, null, null, null, null, null));
        Assert.True(ok.Success, ok.Error);
        Assert.Equal("Oats", ok.Food!.Name);
        Assert.Null(ok.Food.Brand);
        Assert.Empty(await svc.SearchLocalAsync(_bob, "oats"));   // bob has his own foods
        Assert.Single(await svc.SearchLocalAsync(_alice, "oats"));
    }

    [Fact] public async Task ABarcodeIsUniquePerUser_ButADeletedFoodComesBack()
    {
        var svc = Svc();
        var input = new FoodInput("Local jam", null, false, 250, 0, 60, 0, null, null, "5941000099991");
        var first = (await svc.CreateFoodAsync(_alice, input)).Food!;
        Assert.False((await svc.CreateFoodAsync(_alice, input)).Success);
        Assert.True((await svc.CreateFoodAsync(_bob, input)).Success);

        await svc.LogAsync(_alice, first, Meal.Snacks, 10, AmountUnit.Grams, Today);   // entries point to it, so deleting only hides it
        Assert.True((await svc.DeleteFoodAsync(_alice, first.FoodId!.Value)).Success);
        Assert.Empty(await svc.SearchLocalAsync(_alice, "jam"));
        Assert.Equal(LookupStatus.NotFound, (await Svc(Off()).LookupBarcodeAsync(_alice, "5941000099991")).Status);

        var again = await svc.CreateFoodAsync(_alice, input with { Name = "Local jam v2" });
        Assert.True(again.Success, again.Error);
        Assert.Equal(first.FoodId, again.Food!.FoodId);   // the same row, back
        Assert.Equal(LookupStatus.Found, (await Svc(Off()).LookupBarcodeAsync(_alice, "5941000099991")).Status);
    }

    [Fact] public async Task DeletingAFoodWithNoEntries_RemovesIt_AndOnlyOwnFoodsCanBeDeleted()
    {
        var svc = Svc();
        var mine = (await svc.CreateFoodAsync(_alice, new FoodInput("Temp", null, false, 100, null, null, null, null, null))).Food!;
        Assert.True((await svc.DeleteFoodAsync(_alice, mine.FoodId!.Value)).Success);
        await using (var db = _db.CreateDbContext()) Assert.Empty(db.Foods.Where(f => f.Name == "Temp"));

        var saved = (await svc.SetFavouriteAsync(_alice, await Catalog("egg", 1005), true)).Food!;
        Assert.False((await svc.DeleteFoodAsync(_alice, saved.FoodId!.Value)).Success);
    }

    [Fact] public async Task DeletingAFoodWithEntries_KeepsTheEntriesNumbers()
    {
        var svc = Svc();
        var mine = (await svc.CreateFoodAsync(_alice, new FoodInput("Temp bar", null, false, 400, 20, 40, 10, null, null))).Food!;
        await svc.LogAsync(_alice, mine, Meal.Snacks, 50, AmountUnit.Grams, Today);
        await svc.DeleteFoodAsync(_alice, mine.FoodId!.Value);

        var day = await svc.GetDayAsync(_alice, Today);
        Assert.Equal(200, day.Kcal);
        Assert.Equal("Temp bar", day.Entries[0].Name);   // the food is only hidden
    }

    // ------------------------------------------------------------ search

    [Fact] public async Task Search_PutsOwnFoodsFirst_FavouritesThenMostUsed_ThenTheCatalog_WithoutDuplicates()
    {
        var svc = Svc();
        var plain = (await svc.CreateFoodAsync(_alice, new FoodInput("Chicken wrap", null, false, 220, 12, 25, 8, null, null))).Food!;
        var used = (await svc.CreateFoodAsync(_alice, new FoodInput("Chicken salad", null, false, 150, 12, 5, 9, null, null))).Food!;
        var fav = (await svc.CreateFoodAsync(_alice, new FoodInput("Chicken soup", null, false, 40, 4, 3, 1, null, null))).Food!;
        await svc.LogAsync(_alice, used, Meal.Lunch, 100, AmountUnit.Grams, Today);
        await svc.SetFavouriteAsync(_alice, fav, true);
        // a catalog food the user has used already counts once, as their saved copy, among their own foods
        var thigh = await Catalog("chick thigh");
        await svc.LogAsync(_alice, thigh, Meal.Dinner, 100, AmountUnit.Grams, Today);

        var found = await svc.SearchLocalAsync(_alice, "chicken");
        Assert.Equal(["Chicken soup", "Chicken salad", "Chicken, thigh, fried", "Chicken wrap", "Chicken, breast, cooked"],
            found.Select(f => f.Name));
        Assert.Equal(1, found.Count(f => f.Name == "Chicken, thigh, fried"));
        Assert.True(found[0].IsFavourite);
        Assert.Equal([FoodSource.Mine, FoodSource.Mine, FoodSource.Usda, FoodSource.Mine, FoodSource.Usda], found.Select(f => f.Source));
    }

    [Fact] public async Task Search_MatchesBrand_AnyWordOrder_AndTreatsPercentLiterally()
    {
        var svc = Svc();
        await svc.CreateFoodAsync(_alice, new FoodInput("Protein bar", "Acme", false, 400, 30, 40, 10, null, null));
        await svc.CreateFoodAsync(_alice, new FoodInput("Sauce 100% real", null, false, 80, 1, 15, 1, null, null));

        Assert.Single(await svc.SearchLocalAsync(_alice, "acme bar"));
        Assert.Single(await svc.SearchLocalAsync(_alice, "100%"));
        Assert.Empty(await svc.SearchLocalAsync(_alice, "10_ "));   // _ is not a wildcard
    }

    [Fact] public async Task Search_AppendsOpenFoodFacts_AfterTheUsersOwnFoods_WithoutDuplicates()
    {
        // the user already has a food with the barcode of the second hit ("Bretzel")
        await Svc().CreateFoodAsync(_alice, new FoodInput("Covrigi mei", null, false, 300, 8, 50, 5, null, null, "5941487002222"));
        var svc = Svc(Off(("search.openfoodfacts.org", Fixtures.Read("off_search_covrigi.json"))));

        var (foods, online) = await svc.SearchAsync(_alice, "covrigi", "ro");

        Assert.True(online.Success);
        Assert.Equal("Covrigi mei", foods[0].Name);   // their own food first
        Assert.Single(foods, f => f.SourceKey == "5941487002222");   // the hit for that barcode shows as their copy, once
        Assert.DoesNotContain(foods, f => f.Name == "Bretzel");
        Assert.Equal(["Covrigi mei", "Covrigi de buzau", "covrigi din faina dietetica-integrala de grau"], foods.Select(f => f.Name));
        Assert.All(foods.Skip(1), f => Assert.Equal(FoodSource.OpenFoodFacts, f.Source));
        Assert.Equal("Boromir", foods[1].Origin);   // the brand, else "Open Food Facts"
        Assert.Equal("velPitar", foods[2].Origin);
    }

    [Fact] public async Task Search_WhenOffline_StillShowsTheFoodsOnThePhone()
    {
        var svc = Svc(FakeHttpHandler.Throws(new HttpRequestException("no network")));
        var (foods, online) = await svc.SearchAsync(_alice, "chicken");
        Assert.False(online.Success);
        Assert.True(online.Offline);
        Assert.Equal(2, foods.Count);   // the catalog's two chicken foods
    }

    [Fact] public async Task Search_ShortText_SkipsTheNetwork_AndEmptyTextFindsNothing()
    {
        var h = FakeHttpHandler.Json("{\"hits\":[]}");
        var svc = Svc(h);
        var (foods, online) = await svc.SearchAsync(_alice, "eg");
        Assert.True(online.Success);
        Assert.Empty(h.Requests);
        Assert.Empty(await svc.SearchLocalAsync(_alice, "  "));
        Assert.NotEmpty(foods);   // "eg" still finds the egg locally
    }

    [Fact] public async Task ACatalogFoodThatFailsTheRules_ComesBackWithAProblem_AndCantBeLogged()
    {
        var svc = Svc();
        var bad = await Catalog("wrong");
        Assert.Equal(FoodRules.LooksWrong, bad.Problem);

        var r = await svc.LogAsync(_alice, bad, Meal.Snacks, 100, AmountUnit.Grams, Today);
        Assert.False(r.Success);
        Assert.Equal(FoodRules.LooksWrong, r.Error);
        Assert.Null(await LogRow());
        await using var db = _db.CreateDbContext();
        Assert.Empty(db.Foods);   // nothing was saved either
    }

    // ------------------------------------------------------------ recents, favourites, quick +

    [Fact] public async Task Recents_AreTheLast20DistinctFoods_NewestFirst()
    {
        var svc = Svc();
        for (var i = 1; i <= 22; i++)
        {
            var f = (await svc.CreateFoodAsync(_alice, new FoodInput($"Food {i:00}", null, false, 100, null, null, null, null, null))).Food!;
            await svc.LogAsync(_alice, f, Meal.Snacks, 10, AmountUnit.Grams, Today);
            await Task.Delay(2);   // LastUsedAt orders them
        }
        var again = (await svc.SearchLocalAsync(_alice, "Food 05")).First();
        await svc.LogAsync(_alice, again, Meal.Snacks, 10, AmountUnit.Grams, Today);   // used twice, still one row

        var recents = await svc.RecentsAsync(_alice);
        Assert.Equal(20, recents.Count);
        Assert.Equal("Food 05", recents[0].Name);
        Assert.Equal(recents.Count, recents.Select(f => f.FoodId).Distinct().Count());
        Assert.DoesNotContain(recents, f => f.Name == "Food 01");   // fell off the end
    }

    [Fact] public async Task Favourites_AreUnlimited_AndAlphabetical()
    {
        var svc = Svc();
        await svc.SetFavouriteAsync(_alice, await Catalog("egg", 1005), true);
        var oats = (await svc.CreateFoodAsync(_alice, new FoodInput("Banana", null, false, 89, null, null, null, null, null))).Food!;
        await svc.SetFavouriteAsync(_alice, oats, true);

        Assert.Equal(["Banana", "Egg, whole, raw, fresh"], (await svc.FavouritesAsync(_alice)).Select(f => f.Name));
        await svc.SetFavouriteAsync(_alice, oats, false);
        Assert.Single(await svc.FavouritesAsync(_alice));
    }

    [Fact] public async Task QuickPlus_LogsTheLastAmountUsed_InTheMealItWasOpenedFor()
    {
        var svc = Svc();
        var egg = await Catalog("egg", 1005);
        var first = await svc.QuickLogAsync(_alice, egg, Meal.Breakfast, Today);   // never used: one serving
        Assert.Equal(50, first.Entry!.Grams);
        Assert.Equal(1, first.Entry.Servings);

        await svc.LogAsync(_alice, first.Food!, Meal.Breakfast, 3, AmountUnit.Servings, Today);
        var next = await svc.QuickLogAsync(_alice, first.Food!, Meal.Dinner, Today);
        Assert.Equal(150, next.Entry!.Grams);   // 3 servings, as last time
        Assert.Equal(3, next.Entry.Servings);
        Assert.Equal(Meal.Dinner, next.Entry.Meal);

        await svc.LogAsync(_alice, first.Food!, Meal.Snacks, 80, AmountUnit.Grams, Today);
        Assert.Equal(80, (await svc.QuickLogAsync(_alice, first.Food!, Meal.Snacks, Today)).Entry!.Grams);   // last time it was grams

        var apple = await Catalog("apple");
        Assert.Equal(100, (await svc.QuickLogAsync(_alice, apple, Meal.Snacks, Today)).Entry!.Grams);   // no serving: 100 g
    }

    [Fact] public async Task QuickAdd_NeedsNoFood_AndKeepsAnOptionalLabel()
    {
        var svc = Svc();
        var a = (await svc.QuickAddAsync(_alice, 450, "  Restaurant  ", Meal.Dinner, Today)).Entry!;
        var b = (await svc.QuickAddAsync(_alice, 120, null, Meal.Snacks, Today)).Entry!;
        Assert.True(a.IsQuickAdd);
        Assert.Equal("Restaurant", a.Name);
        Assert.Equal("Quick add", b.Name);
        Assert.Null(a.Protein);
        await using var db = _db.CreateDbContext();
        Assert.Empty(db.Foods);
        Assert.False((await svc.QuickAddAsync(_alice, -1, null, Meal.Snacks, Today)).Success);
        Assert.False((await svc.QuickAddAsync(_alice, 100, new string('x', 81), Meal.Snacks, Today)).Success);
    }

    // ------------------------------------------------------------ barcodes

    [Fact] public async Task Barcode_LooksAtTheUsersFoodsFirst_ThenOpenFoodFacts_AndSaysWhyItFailed()
    {
        var svc = Svc(Off(("product/3017620422003", Fixtures.Read("off_product_nutella.json")),
                          ("product/5941000010000", Fixtures.Read("off_not_found.json"))));

        var online = await svc.LookupBarcodeAsync(_alice, "3017620422003");
        Assert.Equal(LookupStatus.Found, online.Status);
        Assert.Null(online.Food!.FoodId);   // not saved just by looking

        // logging copies it; the next scan finds the saved copy without the network
        var logged = await svc.LogAsync(_alice, online.Food, Meal.Snacks, 15, AmountUnit.Grams, Today);
        Assert.True(logged.Success, logged.Error);
        var offline = await Svc(FakeHttpHandler.Throws(new HttpRequestException("no network"))).LookupBarcodeAsync(_alice, "3017620422003");
        Assert.Equal(LookupStatus.Found, offline.Status);
        Assert.NotNull(offline.Food!.FoodId);

        Assert.Equal(LookupStatus.NotFound, (await svc.LookupBarcodeAsync(_alice, "5941000010000")).Status);
        Assert.Equal(LookupStatus.Offline,
            (await Svc(FakeHttpHandler.Throws(new HttpRequestException("no network"))).LookupBarcodeAsync(_alice, "5941000010000")).Status);
        Assert.Equal(LookupStatus.NotFound, (await svc.LookupBarcodeAsync(_alice, "12")).Status);
    }

    [Fact] public async Task AProductWithBadNumbers_IsEditedIntoAMineCopy_AndTheNextScanFindsTheFixedOne()
    {
        var svc = Svc(Off(("product/5941000000024", Fixtures.Read("off_product_wrong.json"))));
        var scanned = (await svc.LookupBarcodeAsync(_alice, "5941000000024")).Food!;
        Assert.True(scanned.NeedsEdit);
        Assert.False((await svc.LogAsync(_alice, scanned, Meal.Snacks, 100, AmountUnit.Grams, Today)).Success);

        var fixedUp = (await svc.SaveFoodEditAsync(_alice, scanned,
            new FoodInput("Test wrong numbers", "Test", false, 120, 5, 20, 3, null, null))).Food!;
        Assert.Equal(FoodSource.Mine, fixedUp.Source);
        Assert.Equal("5941000000024", fixedUp.SourceKey);   // keeps the barcode

        var next = (await svc.LookupBarcodeAsync(_alice, "5941000000024")).Food!;   // the same handler would say 1200 kcal
        Assert.Equal(FoodSource.Mine, next.Source);
        Assert.Equal(120, next.KcalPer100);
        Assert.True((await svc.LogAsync(_alice, next, Meal.Snacks, 100, AmountUnit.Grams, Today)).Success);
    }

    [Fact] public async Task AProductWithoutEnergy_OpensTheEditForm_AndIsLoggedAfterTheNumbersAreAdded()
    {
        var svc = Svc(Off(("product/5941487002932", Fixtures.Read("off_product_no_energy.json"))));
        var scanned = (await svc.LookupBarcodeAsync(_alice, "5941487002932")).Food!;
        Assert.Equal(FoodRules.NoEnergy, scanned.Problem);

        var saved = (await svc.SaveFoodEditAsync(_alice, scanned, new FoodInput("Covrigi de buzau", "Boromir", false, 390, 10, 70, 8, "1 covrig", 80))).Food!;
        var r = await svc.LogAsync(_alice, saved, Meal.Breakfast, 1, AmountUnit.Servings, Today);
        Assert.Equal(312, r.Entry!.Kcal);   // 80 g
    }

    [Fact] public async Task TheUnknownBarcodeShortcut_SavesAFoodThatTheNextScanFinds()
    {
        var svc = Svc(Off(("product/5941000010000", Fixtures.Read("off_not_found.json"))));
        Assert.Equal(LookupStatus.NotFound, (await svc.LookupBarcodeAsync(_alice, "5941000010000")).Status);

        var mine = await svc.CreateFoodAsync(_alice, new FoodInput("Bunica's jam", null, false, 260, 0, 64, 0, "1 spoon", 20, "5941000010000"));
        Assert.True(mine.Success, mine.Error);
        var found = await svc.LookupBarcodeAsync(_alice, "5941000010000");
        Assert.Equal(LookupStatus.Found, found.Status);
        Assert.Equal(FoodSource.Mine, found.Food!.Source);
        Assert.Equal(LookupStatus.NotFound, (await svc.LookupBarcodeAsync(_bob, "5941000010000")).Status);   // per user
    }
}

/// <summary>A small catalog in the same shape as the one tools/food-catalog builds.</summary>
internal static class TestCatalog
{
    public static string Build(bool extra = false)
    {
        var path = Path.Combine(Path.GetTempPath(), $"gymapp-catalog-src-{Guid.NewGuid():N}.db");
        using (var db = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            db.Open();
            Exec(db, """
                CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                CREATE TABLE food (fdc_id INTEGER PRIMARY KEY, name TEXT NOT NULL, source TEXT NOT NULL, kcal REAL NOT NULL,
                    protein REAL, carbs REAL, fat REAL, serving_name TEXT, serving_grams REAL);
                CREATE TABLE portion (fdc_id INTEGER NOT NULL, seq INTEGER NOT NULL, name TEXT NOT NULL, grams REAL NOT NULL);
                CREATE VIRTUAL TABLE food_fts USING fts5(name, content='food', content_rowid='fdc_id',
                    tokenize='unicode61 remove_diacritics 2', prefix='2 3 4');
                """);
            Add(db, 1001, "Chicken, breast, cooked", 165, 31, 0, 3.6, "1 breast", 172);
            Add(db, 1002, "Chicken, thigh, fried", 210, 24, 6, 10, "1 thigh", 90);
            Add(db, 1003, "Bread, oatmeal", 269, 9, 50, 4, "1 slice", 30);
            Add(db, 1004, "Oil, wrong numbers", 884, 60, 60, 100, null, null);   // adds up to 220 g in 100 g
            Add(db, 1005, "Egg, whole, raw, fresh", 143, 12.56, 0.72, 9.51, "1 large", 50, ("1 large", 50), ("1 cup", 243));
            Add(db, 1006, "Apple, raw", 52, 0.3, 14, 0.2, null, null);
            if (extra)
            {
                Add(db, 1007, "Quinoa, cooked", 120, 4.4, 21, 1.9, "1 cup", 185);
                Add(db, 1008, "Eggnog", 224, 6, 21, 10, "1 cup", 254);
            }
            Exec(db, "INSERT INTO food_fts(rowid, name) SELECT fdc_id, name FROM food");
        }
        SqliteConnection.ClearAllPools();
        return path;
    }

    public static Stream Open(string path) => File.OpenRead(path);
    public static Stream Text(string version) => new MemoryStream(Encoding.UTF8.GetBytes(version));

    private static void Add(SqliteConnection db, int id, string name, double kcal, double p, double c, double f, string? servingName, double? servingGrams,
        params (string Name, double Grams)[] portions)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO food VALUES ($id,$n,'test',$k,$p,$c,$f,$sn,$sg)";
        cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$n", name); cmd.Parameters.AddWithValue("$k", kcal);
        cmd.Parameters.AddWithValue("$p", p); cmd.Parameters.AddWithValue("$c", c); cmd.Parameters.AddWithValue("$f", f);
        cmd.Parameters.AddWithValue("$sn", (object?)servingName ?? DBNull.Value); cmd.Parameters.AddWithValue("$sg", (object?)servingGrams ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        var seq = 1;
        foreach (var (pn, pg) in portions)
        {
            using var pc = db.CreateCommand();
            pc.CommandText = "INSERT INTO portion VALUES ($id,$q,$n,$g)";
            pc.Parameters.AddWithValue("$id", id); pc.Parameters.AddWithValue("$q", seq++); pc.Parameters.AddWithValue("$n", pn); pc.Parameters.AddWithValue("$g", pg);
            pc.ExecuteNonQuery();
        }
    }

    private static void Exec(SqliteConnection db, string sql)
    {
        using var c = db.CreateCommand();
        c.CommandText = sql;
        c.ExecuteNonQuery();
    }
}
