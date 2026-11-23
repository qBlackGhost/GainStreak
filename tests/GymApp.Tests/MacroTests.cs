using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

public class MacroRulesTests
{
    [Fact] public void Kcal_IsProteinAndCarbsTimesFour_FatTimesNine_Rounded()
    {
        Assert.Equal(330, FoodRules.KcalFromMacros(30, 30, 10));    // 120 + 120 + 90
        Assert.Equal(0, FoodRules.KcalFromMacros(0, 0, 0));
        Assert.Equal(5, FoodRules.KcalFromMacros(0.3, 0.3, 0.3));   // 1.2 + 1.2 + 2.7 = 5.1
        Assert.Equal(3, FoodRules.KcalFromMacros(0.125, 0.125, 0.25)); // 0.5 + 0.5 + 2.25 = 3.25
        Assert.Equal(1, FoodRules.KcalFromMacros(0.125, 0, 0));     // 0.5 rounds away from zero
    }

    [Fact] public void Validator_NeedsAllThree_ZeroIsFine_EachInRange_SumLimited()
    {
        Assert.Null(FoodRules.CheckMacros(0, 0, 0));
        Assert.NotNull(FoodRules.CheckMacros(null, 1, 1));
        Assert.NotNull(FoodRules.CheckMacros(1, null, 1));
        Assert.NotNull(FoodRules.CheckMacros(1, 1, null));
        Assert.NotNull(FoodRules.CheckMacros(-1, 0, 0));
        Assert.NotNull(FoodRules.CheckMacros(101, 0, 0));
        Assert.NotNull(FoodRules.CheckMacros(double.NaN, 0, 0));
        Assert.Null(FoodRules.CheckMacros(40, 40, 22));              // 102: the slack
        Assert.NotNull(FoodRules.CheckMacros(40, 40, 23));
    }

    [Fact] public void ServingValidator_LimitsByTheServingsWeight()
    {
        Assert.Null(FoodRules.CheckMacrosPerServing(10, 10, 5, 25));
        Assert.NotNull(FoodRules.CheckMacrosPerServing(26, 0, 0, 25));
        Assert.NotNull(FoodRules.CheckMacrosPerServing(10, 10, 10, 25));
        Assert.NotNull(FoodRules.CheckMacrosPerServing(10, null, 5, 25));
    }

    [Fact] public void ProductWithEnergyButMissingMacros_NeedsThem_ButNoEnergyKeepsItsOwnMessage()
    {
        Assert.Equal(FoodRules.NeedsMacros, FoodRules.CheckHasMacros(300, null, 10, 5));
        Assert.Null(FoodRules.CheckHasMacros(300, 0, 0, 0));
        Assert.Null(FoodRules.CheckHasMacros(null, null, null, null));
    }

    [Fact] public void Shares_AddUpTo100_ForAwkwardNumbers()
    {
        var thirds = MacroSplit.Percentages(1, 1, 1);
        Assert.Equal(100, thirds.Sum());
        Assert.Equal([34, 33, 33], thirds);

        Assert.Equal([100, 0, 0], MacroSplit.Percentages(5, 0, 0));
        Assert.Equal([0, 0, 0], MacroSplit.Percentages(0, 0, 0));
        foreach (var (a, b, c) in new[] { (1.0, 2.0, 4.0), (7.0, 11.0, 13.0), (0.1, 0.2, 0.3), (999.0, 1.0, 1.0) })
            Assert.Equal(100, MacroSplit.Percentages(a, b, c).Sum());
    }

    [Fact] public void Split_UsesKcalNotGrams_AndAllZeroHasNoSplit()
    {
        var s = MacroSplit.From(30, 30, 10)!;            // 120 / 120 / 90 of 330
        Assert.Equal(330, s.TotalKcal);
        Assert.Equal([37, 36, 27], [s.ProteinPercent, s.CarbsPercent, s.FatPercent]);   // 36.4 / 36.4 / 27.3: the spare point goes to protein

        Assert.Null(MacroSplit.From(0, 0, 0));
        Assert.Null(MacroSplit.From(null, null, null));
        var one = MacroSplit.From(0, 0, 12)!;
        Assert.Equal([0, 0, 100], [one.ProteinPercent, one.CarbsPercent, one.FatPercent]);
        Assert.Equal(0, MacroSplit.From(20, 0, 0)!.ShareOf(Macro.Fat));
    }

    [Fact] public void BarFill_IsTheShareWithoutATarget_AndGramsOverTargetWithOne_Capped()
    {
        Assert.Equal(0.4, MacroSplit.BarFill(50, null, 0.4, out var over), 6);
        Assert.False(over);
        Assert.Equal(0.5, MacroSplit.BarFill(75, 150, 0.9, out over), 6);
        Assert.False(over);
        Assert.Equal(1, MacroSplit.BarFill(200, 150, 0.9, out over), 6);
        Assert.True(over);
        Assert.Equal(1, MacroSplit.BarFill(150, 150, 0.1, out over), 6);
        Assert.False(over);                                   // exactly at the target is not over
        Assert.Equal(0.25, MacroSplit.BarFill(10, 0, 0.25, out over), 6);   // a zero target counts as none
    }
}

public class MacroServiceTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 21);
    private static readonly DateOnly Yesterday = Today.AddDays(-1);

    private readonly TestDb _db = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gymapp-macro-{Guid.NewGuid():N}");
    private readonly string _catalogFile = TestCatalog.Build();
    private readonly CalorieService _calories;
    private readonly PointsService _points;
    private readonly int _alice;

    public MacroServiceTests()
    {
        _points = new PointsService(_db);
        _calories = new CalorieService(_db, _points);
        _alice = _db.AddUser("alice");
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
            new OpenFoodFactsClient(http ?? FakeHttpHandler.Json("{\"hits\":[]}"), TimeSpan.Zero, TimeSpan.Zero), _calories, _points);

    private static FoodInput Typed(double? p, double? c, double? f, double? kcalIgnored = 9999) =>
        new("Typed bar", null, false, kcalIgnored, p, c, f, null, null);

    // ------------------------------------------------------------ typed foods

    [Fact] public async Task HandTypedFood_NeedsAllThreeMacros()
    {
        var svc = Svc();
        foreach (var input in new[] { Typed(null, 10, 5), Typed(10, null, 5), Typed(10, 10, null), Typed(null, null, null) })
            Assert.False((await svc.CreateTypedFoodAsync(_alice, input)).Success);
        Assert.True((await svc.CreateTypedFoodAsync(_alice, Typed(0, 0, 0))).Success);   // 0 is allowed
    }

    [Fact] public async Task HandTypedFood_KcalComesFromTheMacros_NotFromTheInput()
    {
        var food = (await Svc().CreateTypedFoodAsync(_alice, Typed(30, 30, 10))).Food!;
        Assert.Equal(330, food.KcalPer100);
        var logged = await Svc().LogAsync(_alice, food, Meal.Lunch, 200, AmountUnit.Grams, Today);
        Assert.Equal(660, logged.Entry!.Kcal);
        Assert.Equal(60, logged.Entry.Protein);
    }

    [Fact] public async Task EditingAMineFood_Recalculates_AndLeavesPastEntriesAlone()
    {
        var svc = Svc();
        var food = (await svc.CreateTypedFoodAsync(_alice, Typed(30, 30, 10))).Food!;
        var entry = (await svc.LogAsync(_alice, food, Meal.Lunch, 100, AmountUnit.Grams, Yesterday)).Entry!;

        var edited = (await svc.SaveTypedFoodEditAsync(_alice, food, Typed(10, 10, 10))).Food!;   // 170
        Assert.Equal(food.FoodId, edited.FoodId);
        Assert.Equal(170, edited.KcalPer100);
        Assert.Equal(330, (await svc.GetDayAsync(_alice, Yesterday)).Entries.Single(e => e.Id == entry.Id).Kcal);
        Assert.False((await svc.SaveTypedFoodEditAsync(_alice, edited, Typed(null, 10, 10))).Success);
    }

    [Fact] public async Task HandTypedFood_MacrosAddingUpToMoreThan100g_AreRefused()
    {
        Assert.False((await Svc().CreateTypedFoodAsync(_alice, Typed(60, 60, 0))).Success);   // 120 g in 100 g
    }

    // ------------------------------------------------------------ quick add

    [Fact] public async Task QuickAdd_NeedsAllThreeMacros_AndCalculatesKcal()
    {
        var svc = Svc();
        Assert.False((await svc.QuickAddMacrosAsync(_alice, null, 10, 5, null, Meal.Lunch, Today)).Success);
        Assert.False((await svc.QuickAddMacrosAsync(_alice, 10, 10, null, null, Meal.Lunch, Today)).Success);
        Assert.False((await svc.QuickAddMacrosAsync(_alice, -1, 0, 0, null, Meal.Lunch, Today)).Success);

        var ok = await svc.QuickAddMacrosAsync(_alice, 25, 40, 10, "Canteen", Meal.Lunch, Today);
        Assert.True(ok.Success, ok.Error);
        Assert.Equal(350, ok.Entry!.Kcal);                    // 100 + 160 + 90
        Assert.Equal((25.0, 40.0, 10.0), (ok.Entry.Protein!.Value, ok.Entry.Carbs!.Value, ok.Entry.Fat!.Value));
        Assert.True(ok.Entry.IsQuickAdd);
    }

    [Fact] public async Task EditingAQuickAdd_FromMacros_RecalculatesTheDay()
    {
        var svc = Svc();
        var e = (await svc.QuickAddMacrosAsync(_alice, 25, 40, 10, null, Meal.Lunch, Today)).Entry!;
        var edited = await svc.EditQuickAddMacrosAsync(_alice, e.Id, Meal.Dinner, 10, 10, 10, "Soup", Today);
        Assert.True(edited.Success, edited.Error);
        var day = await svc.GetDayAsync(_alice, Today);
        Assert.Equal(170, day.Kcal);
        Assert.Equal(Meal.Dinner, day.Entries.Single().Meal);
        Assert.False((await svc.EditQuickAddMacrosAsync(_alice, e.Id, Meal.Dinner, 10, null, 10, null, Today)).Success);
    }

    // ------------------------------------------------------------ catalog and Open Food Facts foods

    [Fact] public async Task CatalogFood_KeepsItsOwnKcal()
    {
        var svc = Svc();
        var chicken = (await svc.SearchLocalAsync(_alice, "chicken breast")).First(f => f.Source == FoodSource.Usda);
        Assert.Null(chicken.Problem);
        var logged = await svc.LogAsync(_alice, chicken, Meal.Dinner, 100, AmountUnit.Grams, Today);
        Assert.Equal(165, logged.Entry!.Kcal);               // the label value, not 31 x 4 + 3.6 x 9
    }

    [Fact] public async Task ProductWithEnergyButNoMacros_NeedsTheForm_BeforeItCanBeLogged()
    {
        const string json = """{"code":"5941000077777","product":{"code":"5941000077777","product_name":"Plain thing","nutriments":{"energy-kcal_100g":300}},"status":1}""";
        var svc = Svc(FakeHttpHandler.Json(json));
        var found = (await svc.LookupBarcodeAsync(_alice, "5941000077777")).Food!;
        Assert.Equal(FoodRules.NeedsMacros, found.Problem);
        Assert.False((await svc.LogAsync(_alice, found, Meal.Snacks, 50, AmountUnit.Grams, Today)).Success);

        var saved = await svc.SaveTypedFoodEditAsync(_alice, found, new FoodInput("Plain thing", null, false, null, 20, 30, 5, null, null));
        Assert.True(saved.Success, saved.Error);
        Assert.Equal(245, saved.Food!.KcalPer100);           // 80 + 120 + 45
        Assert.True((await svc.LogAsync(_alice, saved.Food, Meal.Snacks, 50, AmountUnit.Grams, Today)).Success);
    }

    // ------------------------------------------------------------ the day's macros

    [Fact] public async Task DayMacros_AddUpFromSeveralEntriesAndMeals()
    {
        var svc = Svc();
        var food = (await svc.CreateTypedFoodAsync(_alice, Typed(20, 40, 10))).Food!;        // per 100 g
        await svc.LogAsync(_alice, food, Meal.Breakfast, 100, AmountUnit.Grams, Today);       // 20 / 40 / 10
        await svc.LogAsync(_alice, food, Meal.Dinner, 50, AmountUnit.Grams, Today);           // 10 / 20 / 5
        await svc.QuickAddMacrosAsync(_alice, 5, 10, 5, null, Meal.Snacks, Today);

        var m = await svc.GetDayMacrosAsync(_alice, Today);
        Assert.True(m.HasEntries);
        Assert.Equal((35.0, 70.0, 20.0), (m.Protein, m.Carbs, m.Fat));
        Assert.Equal(600, m.Kcal);   // 330 (100 g) + 165 (50 g) + 105 (the quick add)
        Assert.NotNull(m.Split);
        Assert.Equal(100, m.Split!.ProteinPercent + m.Split.CarbsPercent + m.Split.FatPercent);
    }

    [Fact] public async Task DayMacros_ADayWithNoEntries_HasNoSplit_AlsoWhenLoggedAsATotal()
    {
        var svc = Svc();
        var none = await svc.GetDayMacrosAsync(_alice, Today);
        Assert.False(none.HasEntries);
        Assert.Null(none.Split);

        await _calories.LogTodayAsync(_alice, 1800, Yesterday);
        var total = await svc.GetDayMacrosAsync(_alice, Yesterday);
        Assert.False(total.HasEntries);
        Assert.Equal(1800, total.Kcal);
        Assert.Null(total.Split);
    }

    [Fact] public async Task DayMacros_EntriesWithoutMacros_ReadAsZero_AndDontCrash()
    {
        var svc = Svc();
        await svc.QuickAddAsync(_alice, 450, "Old style", Meal.Lunch, Today);                   // no macros stored
        var m = await svc.GetDayMacrosAsync(_alice, Today);
        Assert.True(m.HasEntries);
        Assert.Equal(450, m.Kcal);
        Assert.Equal((0.0, 0.0, 0.0), (m.Protein, m.Carbs, m.Fat));
        Assert.Null(m.Split);
    }
}
