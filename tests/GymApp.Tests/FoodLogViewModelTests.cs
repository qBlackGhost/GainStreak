using System.Net;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using GymApp.App;
using GymApp.App.ViewModels;
using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

public class MealDefaultsTests
{
    [Theory]
    [InlineData(0, 0, Meal.Breakfast)]
    [InlineData(10, 59, Meal.Breakfast)]
    [InlineData(11, 0, Meal.Lunch)]
    [InlineData(15, 59, Meal.Lunch)]
    [InlineData(16, 0, Meal.Dinner)]
    [InlineData(20, 59, Meal.Dinner)]
    [InlineData(21, 0, Meal.Snacks)]
    [InlineData(23, 59, Meal.Snacks)]
    public void TheMealFollowsTheTimeOfDay(int hour, int minute, Meal expected) =>
        Assert.Equal(expected, MealDefaults.ForTime(new TimeOnly(hour, minute)));
}

public class FoodAmountViewModelTests
{
    private static FoodInfo Egg(bool liquid = false) =>
        new(null, FoodSource.Usda, "1005", "Egg", null, liquid, 143, 12.56, 0.72, 9.51, "1 large", 50);

    private static FoodInfo Apple() => new(null, FoodSource.Usda, "1006", "Apple", null, false, 52, 0.3, 14, 0.2, null, null);

    [Fact] public void StartsAtOneServing_WhenTheFoodHasOne_AndAt100gWhenNot()
    {
        var egg = new FoodAmountViewModel(Egg(), UnitSystem.Metric);
        Assert.Equal(["Servings", "g"], egg.Options.Select(o => o.Name));
        Assert.Equal("1", egg.AmountText);
        Assert.Equal("72", egg.KcalText);   // 143 x 0.5 = 71.5, rounded up

        var apple = new FoodAmountViewModel(Apple(), UnitSystem.Metric);
        Assert.Equal(["g"], apple.Options.Select(o => o.Name));   // no Servings without a serving
        Assert.Equal("100", apple.AmountText);
        Assert.Equal("52", apple.KcalText);
    }

    [Fact] public void KcalAndMacros_UpdateWhileTyping()
    {
        var vm = new FoodAmountViewModel(Egg(), UnitSystem.Metric);
        vm.AmountText = "2";
        Assert.Equal("143", vm.KcalText);
        Assert.Equal("12.6", vm.ProteinText);
        Assert.Equal("0.7", vm.CarbsText);
        Assert.Equal("9.5", vm.FatText);
        Assert.Equal("× 1 large (50 g each)", vm.ServingHint);

        vm.AmountText = "2,5";   // a comma works too
        Assert.Equal("179", vm.KcalText);   // 125 g
        Assert.True(vm.IsValid);
        Assert.Equal(125, vm.Grams);
    }

    [Fact] public void BadOrEmptyAmounts_ShowDashes_AndAnError_NotNumbers()
    {
        var vm = new FoodAmountViewModel(Egg(), UnitSystem.Metric);
        vm.AmountText = "";
        Assert.False(vm.IsValid);
        Assert.Equal("—", vm.KcalText);
        Assert.Null(vm.Error);
        Assert.Null(vm.ToAmount());

        vm.AmountText = "lots";
        Assert.NotNull(vm.Error);
        Assert.False(vm.IsValid);

        vm.AmountText = "0";
        Assert.NotNull(vm.Error);
        vm.AmountText = "101";   // servings: 5,050 g is over the limit
        Assert.NotNull(vm.Error);
        vm.AmountText = "3";
        Assert.Null(vm.Error);
        Assert.True(vm.IsValid);
    }

    [Fact] public void ChangingTheUnit_KeepsTheSameAmount()
    {
        var vm = new FoodAmountViewModel(Egg(), UnitSystem.Metric);
        vm.AmountText = "3";                 // 150 g
        vm.SelectedIndex = 1;
        Assert.Equal("150", vm.AmountText);
        Assert.Equal(215, vm.Kcal);          // unchanged: 143 x 1.5, rounded
        Assert.True(vm.Options[1].IsSelected);
        Assert.False(vm.Options[0].IsSelected);

        vm.AmountText = "75";
        vm.SelectedIndex = 0;
        Assert.Equal("1.5", vm.AmountText);
    }

    [Fact] public void ToAmount_GivesServingsAsTyped_AndEverythingElseAsGrams()
    {
        var vm = new FoodAmountViewModel(Egg(), UnitSystem.Metric);
        vm.AmountText = "2";
        Assert.Equal((2.0, AmountUnit.Servings), vm.ToAmount());
        vm.SelectedIndex = 1;
        Assert.Equal((100.0, AmountUnit.Grams), vm.ToAmount());
    }

    [Fact] public void ImperialUsers_GetOunces_ButGramsAreWhatIsLogged()
    {
        var vm = new FoodAmountViewModel(Apple(), UnitSystem.Imperial);
        Assert.Equal(["g", "oz"], vm.Options.Select(o => o.Name));
        vm.SelectedIndex = 1;
        Assert.Equal("3.53", vm.AmountText);   // 100 g
        vm.AmountText = "4";
        Assert.Equal(113.4, vm.Grams!.Value, 1);
        var (amount, unit) = vm.ToAmount()!.Value;
        Assert.Equal(AmountUnit.Grams, unit);
        Assert.Equal(113.4, amount, 1);
        Assert.Equal("59", vm.KcalText);   // 113.4 g x 52 / 100
    }

    [Fact] public void Liquids_AreInMl_AndFlOz()
    {
        var vm = new FoodAmountViewModel(new FoodInfo(null, FoodSource.OpenFoodFacts, "5449000000996", "Cola", null, true, 44, 0, 10.9, 0, "1 portion (330 ml)", 330),
            UnitSystem.Imperial);
        Assert.Equal(["Servings", "ml", "fl oz"], vm.Options.Select(o => o.Name));
        Assert.Equal("145", vm.KcalText);   // one 330 ml serving
        vm.SelectedIndex = 2;
        Assert.Equal("11.16", vm.AmountText);   // 330 ml in fl oz
        Assert.Equal("× 1 portion (330 ml) (330 ml each)", new FoodAmountViewModel(vm.Food, UnitSystem.Metric).ServingHint);
    }

    [Fact] public void EditingAnEntry_StartsFromItsServings_OrItsGrams()
    {
        Assert.Equal("3", new FoodAmountViewModel(Egg(), UnitSystem.Metric, servings: 3, grams: 150).AmountText);
        var g = new FoodAmountViewModel(Egg(), UnitSystem.Metric, servings: null, grams: 80);
        Assert.Equal("80", g.AmountText);
        Assert.Equal(1, g.SelectedIndex);   // grams, since it wasn't logged in servings
        Assert.Equal("1.6", FoodAmountViewModel.Format(1.6));
    }

    [Fact] public void MissingMacros_ShowADash()
    {
        var vm = new FoodAmountViewModel(new FoodInfo(null, FoodSource.Mine, null, "Mystery", null, false, 100, null, null, null, null, null), UnitSystem.Metric);
        Assert.Equal("100", vm.KcalText);
        Assert.Equal("—", vm.ProteinText);
    }
}

public class FoodFormViewModelTests
{
    [Fact] public void PerServing_IsConvertedToPer100g_AndKcalIsCalculatedFromTheMacros()
    {
        var f = new FoodFormViewModel { Name = "Protein bar", ProteinText = "15", CarbsText = "20", FatText = "5",
            ServingName = "1 bar", ServingGramsText = "50", BasisIndex = 1 };
        Assert.Equal("= 185 kcal", f.KcalPreview);   // 60 + 80 + 45, for the serving as typed
        var input = f.TryBuild()!;
        Assert.Null(f.Error);
        Assert.Equal(370, input.Kcal);               // per 100 g: 30 / 40 / 10
        Assert.Equal(30, input.Protein);
        Assert.Equal(40, input.Carbs);
        Assert.Equal(10, input.Fat);
        Assert.Equal(50, input.ServingGrams);
    }

    [Fact] public void PerServing_NeedsTheServingsWeight()
    {
        var f = new FoodFormViewModel { Name = "Bar", ProteinText = "1", CarbsText = "1", FatText = "1", BasisIndex = 1 };
        Assert.Null(f.TryBuild());
        Assert.Contains("serving's weight", f.Error);
    }

    [Fact] public void PerServing_MacrosCantWeighMoreThanTheServing()
    {
        var f = new FoodFormViewModel { Name = "Bar", ProteinText = "30", CarbsText = "0", FatText = "0", ServingGramsText = "25", BasisIndex = 1 };
        Assert.Null(f.TryBuild());
        Assert.Contains("serving", f.Error);
    }

    [Theory]
    [InlineData("", "5", "5", "Enter the protein")]
    [InlineData("5", "", "5", "Enter the carbs")]
    [InlineData("5", "5", "", "Enter the fat")]
    [InlineData("abc", "5", "5", "must be a number")]
    [InlineData("101", "0", "0", "between 0 and 100")]
    [InlineData("60", "60", "0", "more than 100 g")]
    public void Macros_AreRequiredAndValidated(string protein, string carbs, string fat, string message)
    {
        var f = new FoodFormViewModel { Name = "Oats", ProteinText = protein, CarbsText = carbs, FatText = fat };
        Assert.Null(f.TryBuild());
        Assert.Contains(message, f.Error);
    }

    [Fact] public void NameIsRequired_ZeroMacrosAreFine_AndThePreviewFollowsWhatIsTyped()
    {
        Assert.Contains("name", new FoodFormViewModel { Name = " ", ProteinText = "1", CarbsText = "1", FatText = "1" }.Let(f => { f.TryBuild(); return f.Error!; }));
        var water = new FoodFormViewModel { Name = "Water", ProteinText = "0", CarbsText = "0", FatText = "0" };
        Assert.Equal("= 0 kcal", water.KcalPreview);
        Assert.Equal(0, water.TryBuild()!.Kcal);

        var f2 = new FoodFormViewModel { Name = "Oats" };
        Assert.Equal("= — kcal", f2.KcalPreview);
        f2.ProteinText = "10"; f2.CarbsText = "60"; f2.FatText = "7";
        Assert.Equal("= 343 kcal", f2.KcalPreview);   // 40 + 240 + 63
    }

    [Fact] public void AProductWithoutEnergy_OpensWithItsReason_AndItsMacros()
    {
        var product = new FoodInfo(null, FoodSource.OpenFoodFacts, "5941487002932", "Covrigi", "Boromir", false, 0, null, null, null, "400g", 400,
            Problem: FoodRules.NoEnergy);
        var f = new FoodFormViewModel(product, "Check this food", product.Problem);
        Assert.Equal("", f.ProteinText);   // missing macros stay empty: they have to be typed
        Assert.Equal("Covrigi", f.Name);
        Assert.Equal("Boromir", f.Brand);
        Assert.Equal("5941487002932", f.Barcode);
        Assert.Equal(FoodRules.NoEnergy, f.Notice);
        Assert.Equal("400", f.ServingGramsText);
    }
}

internal static class LetExtension
{
    public static R Let<T, R>(this T value, Func<T, R> f) => f(value);
}

[Collection("AppClock")]
public class FoodLogViewModelTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gymapp-vm-{Guid.NewGuid():N}");
    private readonly string _catalogFile = TestCatalog.Build();
    private readonly SessionService _session = new();
    private readonly CalorieService _calories;
    private readonly PointsService _points;
    private readonly ProfileService _profiles;
    private readonly int _alice;
    private int _closed;
    private readonly List<FoodResult> _changes = [];

    public FoodLogViewModelTests()
    {
        _points = new PointsService(_db);
        _calories = new CalorieService(_db, _points);
        _profiles = new ProfileService(_db);
        _alice = _db.AddUser("alice");
        using var db = _db.CreateDbContext();
        _session.SignIn(db.Users.Single(u => u.Id == _alice));
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        File.Delete(_catalogFile);
    }

    private static FakeHttpHandler Off(params (string Url, string Json)[] routes) =>
        new((req, _) =>
        {
            var url = req.RequestUri!.AbsoluteUri;
            var hit = routes.FirstOrDefault(r => url.Contains(r.Url));
            return Task.FromResult(hit.Url is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(hit.Json, Encoding.UTF8, "application/json") });
        });

    private FoodService Food(FakeHttpHandler? http = null) =>
        new(_db, new FoodCatalog(_folder, () => TestCatalog.Open(_catalogFile), () => TestCatalog.Text("t1")),
            new OpenFoodFactsClient(http ?? Off(), TimeSpan.Zero, TimeSpan.Zero), _calories, _points);

    private AddFoodViewModel Sheet(FoodService? food = null, Meal? meal = Meal.Lunch, IBarcodeScanner? scanner = null)
    {
        var vm = new AddFoodViewModel(food ?? Food(), _profiles, _session, scanner ?? new NoBarcodeScanner(), () => _closed++) { OnlineDelay = TimeSpan.Zero };
        vm.Load(meal, r => { _changes.Add(r); return Task.CompletedTask; });
        return vm;
    }

    private async Task<FoodResult> LogEgg(FoodService food, double servings = 2, Meal meal = Meal.Breakfast)
    {
        var egg = (await food.SearchLocalAsync(_alice, "egg")).First();
        return await food.LogAsync(_alice, egg, meal, servings, AmountUnit.Servings);
    }

    private static async Task Run(IRelayCommand command, object? parameter = null) => await ((IAsyncRelayCommand)command).ExecuteAsync(parameter);

    // ------------------------------------------------------------ the sheet

    [Fact] public void TheSheet_StartsOnTheMealForTheTimeOfDay_OrTheOneItWasOpenedFor()
    {
        var saved = AppClock.Offset;
        try
        {
            foreach (var (hour, expected) in new[] { (9, Meal.Breakfast), (13, Meal.Lunch), (18, Meal.Dinner), (23, Meal.Snacks) })
            {
                var target = DateTime.Today.AddHours(hour);
                AppClock.Offset = target - DateTime.Now;
                var vm = Sheet(meal: null);
                Assert.Equal(expected, vm.SelectedMeal);
                Assert.Equal((int)expected, vm.SelectedMealIndex);
                Assert.True(vm.MealOptions[(int)expected].IsSelected);
            }
        }
        finally { AppClock.Offset = saved; }

        var dinner = Sheet(meal: Meal.Dinner);   // opened from Dinner's Add button
        Assert.Equal(Meal.Dinner, dinner.SelectedMeal);
        dinner.MealOptions[3].PickCommand.Execute(null);
        Assert.Equal(Meal.Snacks, dinner.SelectedMeal);
        Assert.False(dinner.MealOptions[2].IsSelected);
    }

    [Fact] public async Task AnEmptyBox_ShowsRecentAndFavourites()
    {
        var food = Food();
        await LogEgg(food);
        await food.SetFavouriteAsync(_alice, (await food.SearchLocalAsync(_alice, "apple")).First(), true);

        var vm = Sheet(food);
        await vm.SearchTask;
        await Task.Delay(50);   // Load() fills them in the background
        Assert.True(vm.ShowSuggestions);
        Assert.Equal(["Egg, whole, raw, fresh"], vm.Recents.Select(r => r.Name));
        Assert.Equal(["Apple, raw"], vm.Favourites.Select(r => r.Name));
        Assert.True(vm.HasRecents && vm.HasFavourites);
    }

    [Fact] public async Task Typing_ShowsFoodsOnThePhoneAtOnce_ThenOpenFoodFactsAfterThePause()
    {
        var vm = Sheet(Food(Off(("search.openfoodfacts.org", Fixtures.Read("off_search_covrigi.json")))));
        vm.SearchText = "covrigi";
        await vm.SearchTask;

        Assert.True(vm.ShowResults);
        Assert.False(vm.IsSearchingOnline);
        Assert.False(vm.IsOffline);
        Assert.Equal(["Covrigi de buzau", "Bretzel", "covrigi din faina dietetica-integrala de grau"], vm.Results.Select(r => r.Name));
        Assert.Equal("Boromir · check the numbers first", vm.Results[0].Subtitle);   // no energy in Open Food Facts
        Assert.False(vm.Results[0].CanQuickAdd);
        Assert.Equal("Boromir · 393 kcal · 1 serving (100 g)", vm.Results[1].Subtitle);
        Assert.Equal("velPitar · 375 kcal / 100 g", vm.Results[2].Subtitle);   // no serving: per 100 g
    }

    [Fact] public async Task ShortText_NeverAsksOpenFoodFacts()
    {
        var http = Off();
        var vm = Sheet(Food(http));
        vm.SearchText = "eg";
        await vm.SearchTask;
        Assert.Equal(["Egg, whole, raw, fresh"], vm.Results.Select(r => r.Name));
        Assert.Empty(http.Requests);
    }

    [Fact] public async Task Offline_KeepsTheFoodsOnThePhone_AndSaysSo()
    {
        var vm = Sheet(Food(FakeHttpHandler.Throws(new HttpRequestException("no network"))));
        vm.SearchText = "chicken";
        await vm.SearchTask;
        Assert.True(vm.IsOffline);
        Assert.True(vm.ShowOfflineNote);
        Assert.Equal(2, vm.Results.Count);
        Assert.False(vm.IsSearchingOnline);
        Assert.Null(vm.OnlineMessage);
    }

    [Fact] public async Task ATypedBarcode_LooksTheProductUp_OrSaysItIsUnknown()
    {
        var food = Food(Off(("product/3017620422003", Fixtures.Read("off_product_nutella.json")), ("product/5941000010000", Fixtures.Read("off_not_found.json"))));
        var vm = Sheet(food);

        vm.SearchText = "3017620422003";
        await vm.SearchTask;
        Assert.Equal("Nutella", Assert.Single(vm.Results).Name);

        vm.SearchText = "5941000010000";
        await vm.SearchTask;
        Assert.Empty(vm.Results);
        Assert.Equal("We couldn't find this barcode.", vm.OnlineMessage);
        Assert.False(vm.NoResults);   // the barcode message says it
    }

    [Fact] public async Task TheQuickPlus_LogsInTheSheetsMeal_StaysOpen_AndTellsTheScreenUnder()
    {
        var food = Food();
        var vm = Sheet(food, Meal.Dinner);
        vm.SearchText = "egg";
        await vm.SearchTask;

        await Run(vm.Results[0].QuickAddCommand);

        Assert.Equal(0, _closed);
        Assert.Equal("Added Egg, whole, raw, fresh · 72 kcal", vm.Notice);   // never used before: one serving
        var change = Assert.Single(_changes);
        Assert.True(change.Success);
        Assert.Equal(Meal.Dinner, change.Entry!.Meal);
        Assert.Equal(72, (await food.GetDayAsync(_alice, AppClock.Today)).Kcal);
    }

    [Fact] public async Task TappingAFood_OpensTheAmount_AndLoggingClosesTheSheet()
    {
        var food = Food();
        var vm = Sheet(food, Meal.Lunch);
        vm.SearchText = "chick bre";
        await vm.SearchTask;
        await Run(vm.Results[0].OpenCommand);

        Assert.True(vm.IsAmountPage);
        Assert.True(vm.CanGoBack);
        Assert.Equal("Log 284 kcal", vm.LogLabel);   // 1 breast, 172 g
        vm.Amount!.AmountText = "2";
        Assert.Equal("Log 568 kcal", vm.LogLabel);   // follows the typing

        await Run(vm.LogAmountCommand);
        Assert.Equal(1, _closed);
        Assert.Single(_changes);
        var day = await food.GetDayAsync(_alice, AppClock.Today);
        Assert.Equal(568, day.Kcal);
        Assert.Equal(Meal.Lunch, day.Entries[0].Meal);
    }

    [Fact] public async Task Back_GoesToTheSearch_WithoutLogging()
    {
        var vm = Sheet();
        vm.SearchText = "egg";
        await vm.SearchTask;
        await Run(vm.Results[0].OpenCommand);
        vm.BackCommand.Execute(null);
        Assert.True(vm.IsSearchPage);
        Assert.Empty(_changes);
        Assert.Equal(0, (await Food().GetDayAsync(_alice, AppClock.Today)).Entries.Count);
    }

    [Fact] public async Task TheStar_SavesTheFood_AndFavouritesIt()
    {
        var food = Food();
        var vm = Sheet(food);
        vm.SearchText = "apple";
        await vm.SearchTask;
        await Run(vm.Results[0].OpenCommand);

        await Run(vm.ToggleFavouriteCommand);
        Assert.True(vm.IsFavourite);
        Assert.Single(await food.FavouritesAsync(_alice));
        Assert.Equal("100", vm.Amount!.AmountText);   // the amount box was kept
        await Run(vm.ToggleFavouriteCommand);
        Assert.Empty(await food.FavouritesAsync(_alice));
    }

    // ------------------------------------------------------------ quick add, new food

    [Fact] public async Task QuickAdd_NeedsAllThreeMacros_ShowsTheKcal_AndTakesAnOptionalLabel()
    {
        var food = Food();
        var vm = Sheet(food, Meal.Snacks);
        vm.ShowQuickCommand.Execute(null);
        Assert.True(vm.IsQuickPage);
        Assert.Equal("= — kcal", vm.QuickKcalPreview);

        vm.QuickProteinText = "abc";
        await Run(vm.SaveQuickCommand);
        Assert.NotNull(vm.QuickError);

        vm.QuickProteinText = "25";
        vm.QuickCarbsText = "40";
        await Run(vm.SaveQuickCommand);   // fat missing
        Assert.Contains("fat", vm.QuickError);
        Assert.Equal(0, _closed);

        vm.QuickFatText = "10";
        Assert.Equal("= 350 kcal", vm.QuickKcalPreview);
        vm.QuickLabel = "Canteen pasta";
        await Run(vm.SaveQuickCommand);
        Assert.Equal(1, _closed);
        var entry = (await food.GetDayAsync(_alice, AppClock.Today)).Entries.Single();
        Assert.True(entry.IsQuickAdd);
        Assert.Equal("Canteen pasta", entry.Name);
        Assert.Equal(Meal.Snacks, entry.Meal);
        Assert.Equal(350, entry.Kcal);
        Assert.Equal(25, entry.Protein);
    }

    [Fact] public async Task NewFood_OpensWithWhatWasTyped_SavesIt_AndGoesToTheAmount()
    {
        var food = Food();
        var vm = Sheet(food);
        vm.SearchText = "Bunica's jam";
        await vm.SearchTask;
        vm.NewFoodCommand.Execute(null);

        Assert.True(vm.IsFormPage);
        Assert.False(vm.ShowMealPill);
        Assert.Equal("Bunica's jam", vm.Form!.Name);

        await Run(vm.SaveFormCommand);   // no macros yet
        Assert.True(vm.IsFormPage);
        Assert.Contains("protein", vm.Form!.Error);

        vm.Form.ProteinText = "0"; vm.Form.CarbsText = "65"; vm.Form.FatText = "0";   // per 100 g: 260 kcal
        vm.Form.ServingName = "1 spoon";
        vm.Form.ServingGramsText = "20";
        await Run(vm.SaveFormCommand);

        Assert.True(vm.IsAmountPage);
        Assert.Equal("Bunica's jam", vm.Amount!.Food.Name);
        Assert.Equal("52", vm.Amount.KcalText);   // one 20 g spoon
        Assert.Single(await food.SearchLocalAsync(_alice, "jam"));
        vm.BackCommand.Execute(null);   // back to the search, not the form
        Assert.True(vm.IsSearchPage);
    }

    [Fact] public async Task ATypedBarcodeNotFound_OffersToSaveItAsMyFood_UnderThatBarcode()
    {
        var food = Food(Off(("product/5941000010000", Fixtures.Read("off_not_found.json"))));
        var vm = Sheet(food);
        vm.SearchText = "5941000010000";
        await vm.SearchTask;

        vm.NewFoodCommand.Execute(null);
        Assert.Equal("5941000010000", vm.Form!.Barcode);
        Assert.Equal("", vm.Form.Name);
        vm.Form.Name = "Jam";
        vm.Form.ProteinText = "0"; vm.Form.CarbsText = "62"; vm.Form.FatText = "0";
        await Run(vm.SaveFormCommand);

        var again = await food.LookupBarcodeAsync(_alice, "5941000010000");
        Assert.Equal(LookupStatus.Found, again.Status);
        Assert.Equal(FoodSource.Mine, again.Food!.Source);
    }

    [Fact] public async Task AProductWithBadNumbers_OpensTheForm_AndSavingMakesAMineCopy()
    {
        var food = Food(Off(("search.openfoodfacts.org", Fixtures.Read("off_search_covrigi.json"))));
        var vm = Sheet(food);
        vm.SearchText = "covrigi";
        await vm.SearchTask;

        await Run(vm.Results[0].OpenCommand);   // Boromir, no energy
        Assert.True(vm.IsFormPage);
        Assert.Equal("Check this food", vm.Title);
        Assert.Equal(FoodRules.NoEnergy, vm.Form!.Notice);

        vm.Form.ProteinText = "10"; vm.Form.CarbsText = "50"; vm.Form.FatText = "8";
        await Run(vm.SaveFormCommand);
        Assert.True(vm.IsAmountPage);
        Assert.Equal(FoodSource.Mine, vm.Amount!.Food.Source);
        Assert.Equal("5941487002932", vm.Amount.Food.SourceKey);   // keeps the barcode
    }

    // ------------------------------------------------------------ editing an entry

    [Fact] public async Task AFoodEntry_OpensAtItsAmount_AndSavesTheChange()
    {
        var food = Food();
        var entry = (await LogEgg(food, servings: 2, meal: Meal.Breakfast)).Entry!;
        var vm = new AddFoodViewModel(food, _profiles, _session, new NoBarcodeScanner(), () => _closed++);
        await vm.LoadEntryAsync(entry, r => { _changes.Add(r); return Task.CompletedTask; });

        Assert.True(vm.IsAmountPage);
        Assert.True(vm.IsEditingEntry);
        Assert.True(vm.CanGoBack);   // back to the sheet's list
        Assert.False(vm.CanEditFood);
        Assert.Equal("2", vm.Amount!.AmountText);
        Assert.Equal(Meal.Breakfast, vm.SelectedMeal);
        Assert.Equal("Save 143 kcal", vm.LogLabel);

        vm.Amount.AmountText = "4";
        vm.MealOptions[2].PickCommand.Execute(null);   // Dinner
        await Run(vm.LogAmountCommand);

        Assert.Equal(0, _closed);        // saved, and back on the sheet's list
        Assert.True(vm.IsSearchPage);
        Assert.False(vm.IsEditingEntry);
        Assert.Equal(1, _changes.Count);
        var day = await food.GetDayAsync(_alice, AppClock.Today);
        Assert.Equal(286, day.Kcal);
        Assert.Equal(Meal.Dinner, day.Entries.Single().Meal);
    }

    [Fact] public async Task DeletingAnEntry_FromTheSheet_RemovesItAndTheDaysLog()
    {
        var food = Food();
        var entry = (await LogEgg(food)).Entry!;
        var vm = new AddFoodViewModel(food, _profiles, _session, new NoBarcodeScanner(), () => _closed++);
        await vm.LoadEntryAsync(entry, r => { _changes.Add(r); return Task.CompletedTask; });

        await Run(vm.DeleteEntryCommand);
        Assert.True(vm.IsSearchPage);
        Assert.False(vm.HasLogged);
        Assert.Empty((await food.GetDayAsync(_alice, AppClock.Today)).Entries);
        await using var db = _db.CreateDbContext();
        Assert.Empty(db.CalorieLogs.Where(c => c.UserId == _alice));
    }

    [Fact] public async Task AQuickAddEntry_OpensAtItsMacros()
    {
        var food = Food();
        var entry = (await food.QuickAddAsync(_alice, 450, "Canteen", Meal.Lunch)).Entry!;
        var vm = new AddFoodViewModel(food, _profiles, _session, new NoBarcodeScanner(), () => _closed++);
        await vm.LoadEntryAsync(entry, r => { _changes.Add(r); return Task.CompletedTask; });

        Assert.True(vm.IsQuickPage);
        Assert.Equal("", vm.QuickProteinText);   // an old Quick add has no macros: they have to be typed
        Assert.Equal("Canteen", vm.QuickLabel);
        vm.QuickProteinText = "25"; vm.QuickCarbsText = "40"; vm.QuickFatText = "10";
        await Run(vm.SaveQuickCommand);
        Assert.Equal(350, (await food.GetDayAsync(_alice, AppClock.Today)).Kcal);
        Assert.Single((await food.GetDayAsync(_alice, AppClock.Today)).Entries);   // edited, not added
    }

    [Fact] public async Task AnEntryWhoseFoodIsGone_StillEditsFromItsOwnNumbers()
    {
        var food = Food();
        var mine = (await food.CreateFoodAsync(_alice, new FoodInput("Temp bar", null, false, 400, 20, 40, 10, null, null))).Food!;
        var entry = (await food.LogAsync(_alice, mine, Meal.Snacks, 50, AmountUnit.Grams)).Entry!;
        await food.DeleteFoodAsync(_alice, mine.FoodId!.Value);   // hidden, entry kept
        await using (var db = _db.CreateDbContext())
        {
            var row = db.Foods.Single(f => f.Id == mine.FoodId);
            db.Foods.Remove(row);   // and now really gone
            await db.SaveChangesAsync();
        }
        var shown = (await food.GetDayAsync(_alice, AppClock.Today)).Entries.Single();

        var vm = new AddFoodViewModel(food, _profiles, _session, new NoBarcodeScanner(), () => _closed++);
        await vm.LoadEntryAsync(shown, r => { _changes.Add(r); return Task.CompletedTask; });
        Assert.Equal("Deleted food", shown.Name);
        Assert.False(shown.IsQuickAdd);
        Assert.True(vm.IsAmountPage);
        Assert.Equal("200", vm.Amount!.KcalText);
        vm.Amount.AmountText = "100";
        Assert.Equal("400", vm.Amount.KcalText);
        await Run(vm.LogAmountCommand);
        Assert.Equal(400, (await food.GetDayAsync(_alice, AppClock.Today)).Kcal);
    }


    // ------------------------------------------------------------ barcodes (26d)

    private sealed class FakeScanner(string? code, string? problem = null, bool supported = true) : IBarcodeScanner
    {
        public bool IsSupported { get; } = supported;
        public string? Problem { get; private set; }
        public int Scans { get; private set; }
        public Task<string?> ScanAsync()
        {
            Scans++;
            Problem = code is null ? problem : null;
            return Task.FromResult(code);
        }
    }

    private FoodService BarcodeFood() => Food(Off(("product/3017620422003", Fixtures.Read("off_product_nutella.json")),
        ("product/5941000010000", Fixtures.Read("off_not_found.json")), ("product/5941000000024", Fixtures.Read("off_product_wrong.json"))));

    [Fact] public void TheCameraButton_ShowsOnlyWhereTheHeadCanScan()
    {
        Assert.True(Sheet(scanner: new FakeScanner(null)).CanScan);
        Assert.False(Sheet(scanner: new FakeScanner(null, supported: false)).CanScan);
        Assert.False(Sheet().CanScan);   // the desktop's no-op
        Assert.False(new NoBarcodeScanner().IsSupported);
    }

    [Fact] public async Task AScannedProduct_OpensAtItsAmount_AndLogs()
    {
        var food = BarcodeFood();
        var scanner = new FakeScanner("3017620422003");
        var vm = Sheet(food, Meal.Snacks, scanner);

        await Run(vm.ScanCommand);

        Assert.Equal(1, scanner.Scans);
        Assert.True(vm.IsAmountPage);
        Assert.Equal("Nutella", vm.Amount!.Food.Name);
        Assert.Equal("Open Food Facts", vm.Amount.Food.Attribution);
        vm.Amount.AmountText = "15";   // 15 g of hazelnut spread
        Assert.Equal("Log 81 kcal", vm.LogLabel);
        await Run(vm.LogAmountCommand);
        Assert.Equal(81, (await food.GetDayAsync(_alice, AppClock.Today)).Kcal);
        Assert.Equal(Meal.Snacks, (await food.GetDayAsync(_alice, AppClock.Today)).Entries[0].Meal);
    }

    [Fact] public async Task Cancelling_LeavesTheSheetWhereItWas_WithNoMessage()
    {
        var scanner = new FakeScanner(null);
        var vm = Sheet(scanner: scanner);
        await Run(vm.ScanCommand);
        Assert.Equal(1, scanner.Scans);
        Assert.True(vm.IsSearchPage);
        Assert.Null(vm.BarcodeError);
        Assert.Equal(0, _closed);
    }

    [Fact] public async Task ARefusedCamera_SaysWhy_AndOffersTheNumberBox()
    {
        var vm = Sheet(scanner: new FakeScanner(null, "Camera access is off. Turn it on in Settings, or enter the number."));
        await Run(vm.ScanCommand);
        Assert.True(vm.IsBarcodePage);
        Assert.Equal("Camera access is off. Turn it on in Settings, or enter the number.", vm.BarcodeError);
        Assert.False(vm.HasUnknownBarcode);
        Assert.True(vm.CanGoBack);
        vm.BackCommand.Execute(null);
        Assert.True(vm.IsSearchPage);
    }

    [Fact] public async Task AnUnknownBarcode_SaysSo_AndOffersMyFoodOrSearch()
    {
        var vm = Sheet(BarcodeFood(), scanner: new FakeScanner("5941000010000"));
        await Run(vm.ScanCommand);

        Assert.True(vm.IsBarcodePage);
        Assert.True(vm.HasUnknownBarcode);
        Assert.Equal("5941000010000", vm.UnknownBarcode);
        Assert.False(vm.IsLookingUp);

        await Task.CompletedTask;
        vm.SearchByNameCommand.Execute(null);
        Assert.True(vm.IsSearchPage);
        Assert.False(vm.HasUnknownBarcode);
        Assert.Equal("", vm.SearchText);
    }

    [Fact] public async Task AddItAsMyFood_SavesUnderTheBarcode_SoTheNextScanFindsIt()
    {
        var food = BarcodeFood();
        var vm = Sheet(food, scanner: new FakeScanner("5941000010000"));
        await Run(vm.ScanCommand);

        vm.AddAsMyFoodCommand.Execute(null);
        Assert.True(vm.IsFormPage);
        Assert.Equal("5941000010000", vm.Form!.Barcode);
        vm.Form.Name = "Bunica's jam";
        vm.Form.ProteinText = "0"; vm.Form.CarbsText = "65"; vm.Form.FatText = "0";
        await Run(vm.SaveFormCommand);
        Assert.True(vm.IsAmountPage);   // straight to logging it

        // a second scan of the same code now finds the user's own food, even offline
        var offline = Sheet(Food(FakeHttpHandler.Throws(new HttpRequestException("no network"))), scanner: new FakeScanner("5941000010000"));
        await Run(offline.ScanCommand);
        Assert.True(offline.IsAmountPage);
        Assert.Equal("Bunica's jam", offline.Amount!.Food.Name);
        Assert.Equal(FoodSource.Mine, offline.Amount.Food.Source);
    }

    [Fact] public async Task BackFromTheForm_ReturnsToTheUnknownBarcodeCard()
    {
        var vm = Sheet(BarcodeFood(), scanner: new FakeScanner("5941000010000"));
        await Run(vm.ScanCommand);
        vm.AddAsMyFoodCommand.Execute(null);
        vm.BackCommand.Execute(null);
        Assert.True(vm.IsBarcodePage);
        Assert.True(vm.HasUnknownBarcode);
    }

    [Fact] public async Task EnterNumber_StartsEmptyEachVisit_ButKeepsTheNumberOfAFailedLookup()
    {
        var vm = Sheet(BarcodeFood());
        vm.ShowBarcodeCommand.Execute(null);
        vm.BarcodeText = "5941000010000";
        await Run(vm.LookUpNumberCommand);
        Assert.True(vm.HasUnknownBarcode);
        Assert.Equal("5941000010000", vm.BarcodeText);   // still there after the failed lookup

        vm.SearchByNameCommand.Execute(null);
        vm.ShowBarcodeCommand.Execute(null);
        Assert.Equal("", vm.BarcodeText);
    }

    [Fact] public async Task EnterNumber_WorksWithoutAScanner_AndFindsAProduct()
    {
        var vm = Sheet(BarcodeFood());   // desktop-like: no scanner
        vm.ShowBarcodeCommand.Execute(null);
        Assert.True(vm.IsBarcodePage);
        Assert.False(vm.CanScan);

        vm.BarcodeText = "3017620422003";
        await Run(vm.LookUpNumberCommand);
        Assert.True(vm.IsAmountPage);
        Assert.Equal("Nutella", vm.Amount!.Food.Name);
        vm.BackCommand.Execute(null);   // back to the search
        Assert.True(vm.IsSearchPage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("59414870029AB")]
    public async Task ANumberThatIsNotABarcode_IsRefusedWithoutAnyLookup(string text)
    {
        var http = Off();
        var vm = Sheet(Food(http));
        vm.ShowBarcodeCommand.Execute(null);
        vm.BarcodeText = text;
        await Run(vm.LookUpNumberCommand);
        Assert.Contains("8 to 14 digits", vm.BarcodeError);
        Assert.True(vm.IsBarcodePage);
        Assert.Empty(http.Requests);
    }

    [Fact] public async Task Offline_ExplainsItself_AndTheSameNumberCanBeTriedAgain()
    {
        var down = true;
        var http = new FakeHttpHandler((_, _) => down
            ? throw new HttpRequestException("no network")
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(Fixtures.Read("off_product_nutella.json"), Encoding.UTF8, "application/json") }));
        var vm = Sheet(Food(http), scanner: new FakeScanner("3017620422003"));

        await Run(vm.ScanCommand);
        Assert.True(vm.IsBarcodePage);
        Assert.Contains("Offline", vm.BarcodeError);
        Assert.False(vm.HasUnknownBarcode);   // not "couldn't find": it may exist
        Assert.Equal("3017620422003", vm.BarcodeText);

        down = false;
        await Run(vm.LookUpNumberCommand);
        Assert.True(vm.IsAmountPage);
        Assert.Null(vm.BarcodeError);
    }

    [Fact] public async Task AProductWithBadNumbers_OpensTheFormWhenScanned()
    {
        var vm = Sheet(BarcodeFood(), scanner: new FakeScanner("5941000000024"));
        await Run(vm.ScanCommand);
        Assert.True(vm.IsFormPage);
        Assert.Equal(FoodRules.LooksWrong, vm.Form!.Notice);
        Assert.Equal("5941000000024", vm.Form.Barcode);
    }

    [Fact] public async Task ATypedBarcodeInTheSearchBox_StillWorks()
    {
        var vm = Sheet(BarcodeFood());
        vm.SearchText = "3017620422003";
        await vm.SearchTask;
        Assert.Equal("Nutella", Assert.Single(vm.Results).Name);
    }

    // ------------------------------------------------------------ the Calories screen

    private CaloriesViewModel Screen(bool foodLog, FoodService? food = null) =>
        new(_calories, _profiles, _session, () => throw new InvalidOperationException("no shell in this test"), food ?? Food(),
            new FakePlatform { HasFoodLog = foodLog });

    [Fact] public async Task OnThePhones_TheScreenShowsTodaysMacrosAndHowManyItemsAreLogged()
    {
        var food = Food();
        await LogEgg(food, 2, Meal.Breakfast);
        await food.QuickAddMacrosAsync(_alice, 25, 40, 10, "Canteen", Meal.Lunch);

        var vm = Screen(foodLog: true, food);
        await vm.Loaded;

        Assert.True(vm.HasFoodLog);
        Assert.False(vm.ShowLegacyLogForm);
        Assert.False(vm.ShowLegacyResult);
        Assert.True(vm.HasItems);   // Manage food opens the sheet at today's list
        Assert.True(vm.HasItems);
        Assert.Equal("38 g", vm.ProteinBar.GramsText);   // 12.6 + 25 = 37.6, rounded
        Assert.Equal("41 g", vm.CarbsBar.GramsText);
        Assert.Equal("20 g", vm.FatBar.GramsText);
        Assert.Equal("493", vm.EatenKcal);   // the ring still reads CalorieLog: 143 + 350
        Assert.Equal("No goal yet · eaten 493", vm.GoalEatenText);
    }

    [Fact] public async Task WithoutTargets_EachBarIsItsShareOfTheDaysCalories_AndTheThreeAddUpToOne()
    {
        var food = Food();
        await food.QuickAddMacrosAsync(_alice, 30, 30, 10, null, Meal.Lunch);   // 120 / 120 / 90 kcal of 330
        var vm = Screen(foodLog: true, food);
        await vm.Loaded;
        Assert.Equal(120.0 / 330, vm.ProteinBar.Fill, 6);
        Assert.Equal(120.0 / 330, vm.CarbsBar.Fill, 6);
        Assert.Equal(90.0 / 330, vm.FatBar.Fill, 6);
        Assert.Equal(1.0, vm.ProteinBar.Fill + vm.CarbsBar.Fill + vm.FatBar.Fill, 6);
        Assert.False(vm.ProteinBar.IsOver);
    }

    [Fact] public async Task WithTargets_TheBarIsGramsOverTheTarget_CappedAndMarkedOver()
    {
        var food = Food();
        Assert.True((await _calories.SetCustomMacrosAsync(_alice, GoalType.Maintain, 60, 250, 10)).Success);   // 1,330 kcal
        await food.QuickAddMacrosAsync(_alice, 30, 30, 20, null, Meal.Lunch);
        var vm = Screen(foodLog: true, food);
        await vm.Loaded;
        Assert.Equal(0.5, vm.ProteinBar.Fill, 6);   // 30 of 60
        Assert.Equal(0.12, vm.CarbsBar.Fill, 6);    // 30 of 250
        Assert.Equal(1.0, vm.FatBar.Fill, 6);       // 20 of 10: capped
        Assert.True(vm.FatBar.IsOver);
        Assert.False(vm.ProteinBar.IsOver);
    }

    [Fact] public async Task ADayWithNoFood_HasEmptyBars_AndNothingToOpen()
    {
        var vm = Screen(foodLog: true);
        await vm.Loaded;
        Assert.False(vm.HasItems);
        Assert.False(vm.HasItems);
        Assert.Equal(0, vm.ProteinBar.Fill);
        Assert.Equal("0 g", vm.FatBar.GramsText);
    }

    [Fact] public async Task ADayLoggedAsATotal_HasNoItemsAndNoMacros()
    {
        await _calories.LogTodayAsync(_alice, 1800);
        var vm = Screen(foodLog: true);
        await vm.Loaded;
        Assert.False(vm.HasItems);
        Assert.Equal("0 g", vm.ProteinBar.GramsText);
        Assert.Equal("1,800", vm.EatenKcal);
    }

    [Fact] public async Task OnTheDesktop_TheOldFormStays_AndThereAreNoMacroBars()
    {
        var vm = Screen(foodLog: false);
        await vm.Loaded;
        Assert.False(vm.HasFoodLog);
        Assert.True(vm.ShowLegacyLogForm);
        Assert.False(vm.ShowLegacyResult);
        Assert.False(vm.HasItems);
        Assert.All(vm.Bars, b => Assert.False(b.IsSelected));   // no outline on the desktop's chart

        vm.TodayInput = "1900";
        await Run(vm.LogCommand);
        Assert.True(vm.IsLogged);
        Assert.True(vm.ShowLegacyResult);
        Assert.False(vm.ShowLegacyLogForm);
        Assert.Equal("1,900", vm.EatenKcal);
    }

    [Fact] public async Task TheChart_OutlinesToday_CountsOnTargetDays_AndSizesBarsAsSharesOfItsHeight()
    {
        await _calories.SetCustomGoalAsync(_alice, 2000);
        await _calories.LogTodayAsync(_alice, 2000);
        var vm = Screen(foodLog: true);
        await vm.Loaded;
        Assert.Equal("1 on target", vm.OnTargetText);
        Assert.Equal([AppClock.Today], vm.Bars.Where(b => b.IsSelected).Select(b => b.Date));
        var today = vm.Bars.Single(b => b.Date == AppClock.Today);
        Assert.Equal(2000.0 / 3000, today.Fraction, 6);
        Assert.InRange(vm.BandBottomFraction, 0.5, 0.7);   // the target band starts below 2000 of 3000
        Assert.True(vm.BandHeightFraction > 0);
        Assert.All(vm.Bars.Where(b => !b.IsLogged), b => Assert.InRange(b.Fraction, 0.01, 0.1));
    }

    [Fact] public async Task TappingAPastDaysBar_OpensItsPopup_AndTodaysDoesNothing()
    {
        var opened = new List<DateOnly>();
        var vm = Screen(foodLog: true);
        vm.PastDayOpener = opened.Add;
        await vm.Loaded;

        var yesterday = AppClock.Today.AddDays(-1);
        await Run(vm.ShowDayCommand, yesterday);
        await Run(vm.ShowDayCommand, AppClock.Today);
        Assert.Equal([yesterday], opened);
    }

    [Fact] public async Task TheGoalAndMaintenanceRows_SaySomethingUseful()
    {
        var vm = Screen(foodLog: true);
        await vm.Loaded;
        Assert.Equal("Not set", vm.GoalRowText);
        Assert.Equal("Add body data", vm.MaintenanceRowText);

        await _calories.SetCustomGoalAsync(_alice, 2200);
        vm = Screen(foodLog: true);
        await vm.Loaded;
        Assert.Equal("2,200 kcal", vm.GoalRowText);
    }

    // ------------------------------------------------------------ the Calorie goal sheet: Recommended and Custom

    // Male, 30, 180 cm, 80 kg, Active: maintenance 2,760 kcal
    private async Task FillBodyDataAsync()
    {
        await _profiles.SaveAsync(_alice, new ProfileInput(Sex.Male, AppClock.Today.AddYears(-30), 180, ActivityLevel.Active, UnitSystem.Metric));
        await _profiles.LogWeightAsync(_alice, 80);
    }

    private async Task<(CalorieGoalSheetViewModel Sheet, Func<int> Saved, Func<int> Closed, Func<int> BodyData)> GoalSheetAsync()
    {
        int saved = 0, closed = 0, body = 0;
        var info = (await _calories.GetAsync(_alice))!.Goal;
        var vm = new CalorieGoalSheetViewModel(info, _calories, _alice, () => { saved++; return Task.CompletedTask; }, () => closed++, () => body++);
        return (vm, () => saved, () => closed, () => body);
    }

    [Fact] public async Task TheGoalSheet_RecommendsMacrosPerKg_AndTheCaloriesTheyAddUpTo()
    {
        await FillBodyDataAsync();
        var (vm, saved, closed, _) = await GoalSheetAsync();
        Assert.True(vm.ShowRecommendation);
        Assert.Equal("Use these targets", vm.SaveLabel);
        Assert.Equal(GoalType.Maintain, vm.GoalType);
        Assert.Equal(["144 g", "320 g", "72 g"], vm.Tiles.Select(t => t.GramsText));
        Assert.Equal(["1.8 g per kg", "4.0 g per kg", "0.9 g per kg"], vm.Tiles.Select(t => t.RateText));
        Assert.Equal("2,504", vm.RecommendedKcalText);
        Assert.StartsWith("256 under your maintenance (2,760). For a man of 80 kg, active.", vm.RecommendedNote);

        vm.GoalTypes[0].PickCommand.Execute(null);   // Lose: under the safe minimum, so carbs make up the rest
        Assert.Equal(0, vm.GoalTypeIndex);
        Assert.Equal("raised to the safe minimum", vm.Tiles[1].RateText);
        Assert.Equal("2,072", vm.RecommendedKcalText);

        await Run(vm.SaveCommand);
        Assert.Equal((1, 1), (saved(), closed()));
        var g = (await _calories.GetAsync(_alice))!.Goal;
        Assert.Equal((MacroMode.Recommended, GoalType.Lose, 2072), (g.MacroMode!.Value, g.GoalType, g.Goal!.Value));
    }

    [Fact] public async Task TheGoalSheet_Custom_WorksTheGoalOutFromTheMacros_AndNeedsAllThree()
    {
        await FillBodyDataAsync();
        var (vm, saved, _, _) = await GoalSheetAsync();
        vm.Modes[1].PickCommand.Execute(null);
        Assert.True(vm.IsCustom);
        Assert.Equal("Save", vm.SaveLabel);
        Assert.Equal("—", vm.CustomKcalText);

        vm.ProteinInput = "170"; vm.CarbsInput = "250";
        Assert.Equal("—", vm.CustomKcalText);
        await Run(vm.SaveCommand);
        Assert.Equal("Enter your fat in grams.", vm.Error);
        Assert.Equal(0, saved());

        vm.FatInput = "70";   // 680 + 1,000 + 630
        Assert.Equal("= 2,310", vm.CustomKcalText);
        Assert.Equal("Worked out from your macros. 450 under your maintenance (2,760).", vm.CustomNote);
        await Run(vm.SaveCommand);
        Assert.Equal(1, saved());
        var g = (await _calories.GetAsync(_alice))!.Goal;
        Assert.Equal((MacroMode.Custom, 2310, 170), (g.MacroMode!.Value, g.Goal!.Value, g.ProteinGoalGrams!.Value));

        // opening again starts in Custom with the saved grams
        (vm, _, _, _) = await GoalSheetAsync();
        Assert.True(vm.IsCustom);
        Assert.Equal(("170", "250", "70"), (vm.ProteinInput, vm.CarbsInput, vm.FatInput));
    }

    [Fact] public async Task TheGoalSheet_StartFromRecommended_FillsTheCustomFields_AndBadGramsSayHow()
    {
        await FillBodyDataAsync();
        var (vm, _, _, _) = await GoalSheetAsync();
        vm.Modes[1].PickCommand.Execute(null);
        vm.StartFromRecommendedCommand.Execute(null);
        Assert.Equal(("144", "320", "72"), (vm.ProteinInput, vm.CarbsInput, vm.FatInput));

        vm.CarbsInput = "lots";
        await Run(vm.SaveCommand);
        Assert.Equal("Enter the macros as whole numbers of grams, e.g. 150.", vm.Error);
    }

    [Fact] public async Task TheGoalSheet_WithoutBodyData_StartsInCustom_AndRecommendedOffersBodyData()
    {
        var (vm, _, closed, body) = await GoalSheetAsync();
        Assert.True(vm.IsCustom);
        Assert.False(vm.HasRecommendation);

        vm.Modes[0].PickCommand.Execute(null);
        Assert.True(vm.ShowNoRecommendation);
        Assert.Contains("gender", vm.NoRecommendationText);
        vm.AddBodyDataCommand.Execute(null);
        Assert.Equal((1, 1), (closed(), body()));
    }

    [Fact] public async Task TheGoalRow_SaysWhichModeTheGoalFollows()
    {
        await FillBodyDataAsync();
        await _calories.UseRecommendedAsync(_alice, GoalType.Gain);
        var vm = Screen(foodLog: true);
        await vm.Loaded;
        Assert.Equal("Gain · recommended", vm.GoalRowDetail);
        Assert.True(vm.HasMacroTargets);
        Assert.Equal("2,760 kcal", vm.MaintenanceRowText.TrimStart('≈', ' '));
    }

    // ------------------------------------------------------------ Logged today, in the Add food sheet

    [Fact] public async Task LoggedToday_ListsTheDaysFoodByMeal_WithTheTotal()
    {
        var food = Food();
        await LogEgg(food, 2, Meal.Breakfast);
        await food.QuickAddMacrosAsync(_alice, 25, 40, 10, "Canteen", Meal.Lunch);
        var vm = Sheet(food);
        for (var i = 0; i < 50 && !vm.HasLogged; i++) await Task.Delay(20);

        Assert.True(vm.HasLogged);
        Assert.False(vm.IsReadOnlyDay);
        Assert.Equal("LOGGED TODAY", vm.LoggedTitle);
        Assert.Equal(["Breakfast", "Lunch"], vm.LoggedMeals.Select(m => m.Name));   // only the meals that have something
        Assert.Equal("143", vm.LoggedMeals[0].KcalText);
        Assert.Equal("350", vm.LoggedMeals[1].KcalText);
        Assert.Equal("2 × 1 large · 100 g", vm.LoggedMeals[0].Entries.Single().Detail);
        Assert.Equal("Quick add · 25 P · 40 C · 10 F", vm.LoggedMeals[1].Entries.Single().Detail);
        Assert.Equal("493 kcal", vm.LoggedTotalText);
        Assert.Equal("Protein 37.6 g · Carbs 40.7 g · Fat 19.5 g", vm.LoggedMacroText);
    }

    [Fact] public async Task TappingALoggedItem_OpensItsAmountPage_WithSaveAndRemove()
    {
        var food = Food();
        await LogEgg(food, 2, Meal.Breakfast);
        var vm = Sheet(food);
        for (var i = 0; i < 50 && !vm.HasLogged; i++) await Task.Delay(20);

        await Run(vm.LoggedMeals[0].Entries[0].EditCommand);
        Assert.True(vm.IsAmountPage);
        Assert.True(vm.IsEditingEntry);
        Assert.Equal("Save 143 kcal", vm.LogLabel);

        vm.BackCommand.Execute(null);            // Back returns to the list without changing anything
        Assert.True(vm.IsSearchPage);
        Assert.Single(vm.LoggedMeals);
    }

    [Fact] public async Task APastDay_OpensTheListReadOnly_WithNoSearchAndNoEditing()
    {
        var food = Food();
        var yesterday = AppClock.Today.AddDays(-1);
        await food.QuickAddMacrosAsync(_alice, 30, 50, 20, "Dinner out", Meal.Dinner, yesterday);
        await food.LogAsync(_alice, (await food.SearchLocalAsync(_alice, "egg")).First(), Meal.Breakfast, 2, AmountUnit.Servings, yesterday);
        await food.QuickAddMacrosAsync(_alice, 5, 5, 5, null, Meal.Snacks);   // today: must not show

        var vm = Sheet(food);
        await vm.LoadPastDayAsync(yesterday);

        Assert.True(vm.IsReadOnlyDay);
        Assert.False(vm.ShowSearchTools);
        Assert.False(vm.ShowMealPill);
        Assert.Equal(yesterday.ToString("dddd, d MMMM", System.Globalization.CultureInfo.InvariantCulture), vm.Title);
        Assert.Equal("FOOD LOGGED", vm.LoggedTitle);
        Assert.Equal(["Breakfast", "Dinner"], vm.LoggedMeals.Select(m => m.Name));
        Assert.All(vm.LoggedMeals.SelectMany(m => m.Entries), e => Assert.False(e.CanEdit));
        Assert.Equal("143", vm.LoggedMeals[0].KcalText);
    }

    [Fact] public async Task APastDayWithNothingLogged_SaysSo_AndATotalOnlyDayExplainsItself()
    {
        var vm = Sheet(Food());
        await vm.LoadPastDayAsync(AppClock.Today.AddDays(-3));
        Assert.True(vm.LoggedIsEmpty);
        Assert.Empty(vm.LoggedMeals);

        await _calories.LogTodayAsync(_alice, 1800, AppClock.Today.AddDays(-2));
        await vm.LoadPastDayAsync(AppClock.Today.AddDays(-2));
        Assert.True(vm.LoggedIsTotalOnly);
        Assert.Equal("1,800 kcal", vm.LoggedTotalText);
        Assert.Empty(vm.LoggedMeals);
    }

    // ------------------------------------------------------------ the past-day popup (27d)

    private Task<PastDayPopupViewModel> Popup(FoodService food, DateOnly date, Action? seeFood = null, Action? close = null) =>
        PastDayPopupViewModel.LoadAsync(_calories, food, _alice, date, seeFood ?? (() => { }), close ?? (() => { }));

    [Fact] public async Task APastDayWithFood_ShowsTheRingSplit_ALegendAddingTo100_AndItsStatusAgainstThatDaysGoal()
    {
        var food = Food();
        var day = AppClock.Today.AddDays(-2);
        await _calories.SetCustomGoalAsync(_alice, 1900);
        await food.QuickAddMacrosAsync(_alice, 150, 300, 60, "Big day", Meal.Dinner, day);   // 600 + 1,200 + 540 = 2,340 kcal
        await food.QuickAddMacrosAsync(_alice, 1, 1, 1, null, Meal.Snacks, day);

        var vm = await Popup(food, day);

        Assert.Equal(day.ToString("ddd d MMM", System.Globalization.CultureInfo.InvariantCulture), vm.Title);
        Assert.True(vm.HasEntries);
        Assert.True(vm.ShowRing);
        Assert.False(vm.IsTotalOnly);
        Assert.NotNull(vm.Split);
        Assert.Equal(2357, vm.EatenKcal);
        Assert.Equal(1900, vm.GoalKcal);
        Assert.True(vm.IsOver);
        Assert.Equal("Over target · goal 1,900", vm.StatusText);
        Assert.Equal(["Protein", "Carbs", "Fat"], vm.Legend.Select(r => r.Name));
        Assert.Equal(["151 g", "301 g", "61 g"], vm.Legend.Select(r => r.GramsText));
        Assert.Equal(100, vm.Legend.Sum(r => int.Parse(r.PercentText.TrimEnd('%'))));
    }

    [Fact] public async Task APastDayOnTarget_SaysSo_AndADayWithNoGoalSaysItWasLogged()
    {
        var food = Food();
        var noGoal = AppClock.Today.AddDays(-3);
        await food.QuickAddMacrosAsync(_alice, 10, 10, 10, null, Meal.Lunch, noGoal);
        Assert.Equal("Logged · no goal that day", (await Popup(food, noGoal)).StatusText);

        var hit = AppClock.Today.AddDays(-1);
        await _calories.SetCustomGoalAsync(_alice, 2000);
        await food.QuickAddMacrosAsync(_alice, 125, 250, 56, null, Meal.Lunch, hit);   // 500 + 1,000 + 504 = 2,004 kcal
        var vm = await Popup(food, hit);
        Assert.True(vm.IsHit);
        Assert.Equal("On target", vm.StatusText);
    }

    [Fact] public async Task APastDayLoggedAsATotal_ShowsTheKcalWithNoSplit_AndAnEmptyDaySaysNotLogged()
    {
        var food = Food();
        var total = AppClock.Today.AddDays(-2);
        await _calories.LogTodayAsync(_alice, 1800, total);

        var vm = await Popup(food, total);
        Assert.True(vm.IsTotalOnly);
        Assert.True(vm.ShowRing);
        Assert.Null(vm.Split);
        Assert.Empty(vm.Legend);
        Assert.Equal(1800, vm.EatenKcal);
        Assert.Equal("1,800 kcal", vm.TotalText);
        Assert.False(vm.HasEntries);   // nothing to list, so no "See what you ate"

        var empty = await Popup(food, AppClock.Today.AddDays(-4));
        Assert.False(empty.ShowRing);
        Assert.True(empty.IsNotLogged);
        Assert.Equal("Not logged", empty.StatusText);
        Assert.Empty(empty.Legend);
    }

    [Fact] public async Task SeeWhatYouAte_ClosesThePopupAndOpensTheList_AndCloseOnlyCloses()
    {
        var food = Food();
        var day = AppClock.Today.AddDays(-1);
        await food.QuickAddMacrosAsync(_alice, 20, 20, 20, null, Meal.Lunch, day);
        int seen = 0, closed = 0;
        var vm = await Popup(food, day, () => seen++, () => closed++);

        vm.SeeFoodCommand.Execute(null);
        Assert.Equal((1, 1), (seen, closed));
        vm.CloseCommand.Execute(null);
        Assert.Equal((1, 2), (seen, closed));
    }

    [Fact] public async Task OpeningFromManageFood_WithItems_AsksTheSheetToScrollToTheList()
    {
        var vm = Sheet(Food());
        Assert.Equal(0, vm.ScrollToLoggedRequest);
        vm.Load(null, _ => Task.CompletedTask, scrollToLogged: true);
        Assert.Equal(1, vm.ScrollToLoggedRequest);
    }
}

[CollectionDefinition("AppClock", DisableParallelization = true)]
public class AppClockCollection;
