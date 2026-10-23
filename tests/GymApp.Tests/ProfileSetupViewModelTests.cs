using GymApp.Core;
using GymApp.Desktop.ViewModels;
using GymApp.Services;

namespace GymApp.Tests;

public class ProfileSetupViewModelTests : IDisposable
{
    private readonly TestDb _db = TestDb.OnDisk();
    private readonly SessionService _session = new();
    private readonly int _me;

    public ProfileSetupViewModelTests()
    {
        _me = _db.AddUser("cristi");
        using var db = _db.CreateDbContext();
        _session.SignIn(db.Users.Single(u => u.Id == _me), newAccount: true);
    }

    public void Dispose() => _db.Dispose();

    private ProfileSetupViewModel Setup() => new(new ProfileService(_db), new CalorieService(_db, new PointsService(_db)),
        new GeocodingService(FakeHttpHandler.Json("[]"), TimeSpan.Zero), new CityService(_db), _session, () => null!);

    private static void FillAll(ProfileSetupViewModel vm)
    {
        vm.SelectGenderCommand.Execute("0");
        vm.BirthDayText = "24";
        vm.BirthMonth = vm.Months[7];
        vm.BirthYearText = "2000";
        vm.HeightCmText = "180";
        vm.WeightText = "80";
        vm.SelectedActivity = vm.ActivityOptions[0];
    }

    [Fact] public void Empty_CannotSave_AndSaysWhy()
    {
        var vm = Setup();

        Assert.False(vm.CanSave);
        Assert.False(vm.SaveCommand.CanExecute(null));
        Assert.Equal("Nothing to save yet. Fill in anything above, or skip.", vm.SaveHint);
        Assert.Equal("0 / 5", vm.FilledText);
        Assert.Equal(-1, vm.GenderIndex);
        Assert.Equal(-1, vm.ActivityIndex);
        Assert.Equal("WELCOME, CRISTI", vm.WelcomeText);
    }

    [Fact] public void Estimate_ShowsOnceEverythingIsIn_AndListsWhatIsMissing()
    {
        var vm = Setup();
        vm.HeightCmText = "180";
        Assert.False(vm.HasEstimate);
        Assert.Equal("Add your gender, birth date, weight and activity to see how many calories you burn in a day.", vm.EstimateMessage);
        Assert.Equal("1 / 5", vm.FilledText);

        FillAll(vm);

        var age = BodyMetrics.AgeOn(new DateOnly(2000, 8, 24), AppClock.Today);
        var kcal = BodyMetrics.Maintenance(Sex.Male, 80, 180, age, ActivityLevel.LessActive);
        Assert.True(vm.HasEstimate);
        Assert.Equal("≈ " + kcal.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), vm.EstimateText);
        Assert.Equal("5 / 5", vm.FilledText);
        Assert.All(vm.Progress, Assert.True);
    }

    [Fact] public void PreferNotToSay_CountsAsAnswered_ButGivesNoEstimate()
    {
        var vm = Setup();
        FillAll(vm);

        vm.SelectGenderCommand.Execute("2");

        Assert.Equal("5 / 5", vm.FilledText);
        Assert.False(vm.HasEstimate);
        Assert.StartsWith("The formula uses male or female", vm.EstimateMessage);
        Assert.True(vm.CanSave);
    }

    [Fact] public void BadValues_AreShownUnderTheField_AndBlockSaving()
    {
        var vm = Setup();
        vm.HeightCmText = "40";
        vm.WeightText = "abc";

        Assert.NotNull(vm.HeightError);
        Assert.Equal("Weight must be a number in kg.", vm.WeightError);
        Assert.False(vm.CanSave);
        Assert.True(vm.SaveHintIsError);

        vm.HeightCmText = "175";
        vm.WeightText = "70";
        Assert.Null(vm.HeightError);
        Assert.Null(vm.WeightError);
        Assert.True(vm.CanSave);
    }

    [Fact] public void AHalfTypedBirthDate_IsNoErrorYet()
    {
        var vm = Setup();
        vm.BirthDayText = "12";
        vm.BirthMonth = vm.Months[2];
        vm.BirthYearText = "19";

        Assert.Null(vm.BirthDateError);
        Assert.Null(vm.AgeText);

        vm.BirthYearText = "1990";
        Assert.NotNull(vm.AgeText);

        vm.BirthDayText = "31";
        vm.BirthMonth = vm.Months[1];
        Assert.Equal("February doesn't have 31 days.", vm.BirthDateError);
    }

    [Fact] public void SwitchingUnits_ConvertsWhatIsTyped()
    {
        var vm = Setup();
        vm.HeightCmText = "180";
        vm.WeightText = "80";

        vm.SelectImperialCommand.Execute(null);

        Assert.Equal(("5", "11"), (vm.HeightFtText, vm.HeightInText));
        Assert.Equal("lb", vm.WeightUnit);
        Assert.Equal(Units.WeightInputText(80, UnitSystem.Imperial), vm.WeightText);
        Assert.Null(vm.WeightError);
    }

    [Fact] public async Task Save_StoresProfileWeighInAndCity_ThenShowsTheSummary()
    {
        var vm = Setup();
        FillAll(vm);
        vm.ChosenCity = new CityCandidate(OsmElementType.Relation, 1, "Timișoara", "Timișoara, Timiș, România",
            45.75, 21.22, 45.68, 21.11, 45.83, 21.33);
        Assert.Equal("Timiș, România", vm.CityRegion);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.True(vm.IsDone, vm.Error);
        using var db = _db.CreateDbContext();
        var p = db.UserProfiles.Single(x => x.UserId == _me);
        Assert.Equal((Sex.Male, new DateOnly(2000, 8, 24), 180.0, ActivityLevel.LessActive), (p.Sex, p.BirthDate, p.HeightCm, p.ActivityLevel));
        Assert.Equal("Timișoara", db.Cities.Single(c => c.Id == p.HomeCityId).Name);
        var w = db.WeightLogs.Single(x => x.UserId == _me);
        Assert.Equal((80.0, AppClock.Today), (w.WeightKg, w.Date));

        Assert.Equal(["Gender", "Age", "Height", "Weight", "Activity", "Home city"], vm.Summary.Select(r => r.Label));
        Assert.All(vm.Summary, r => Assert.True(r.IsSet));
        Assert.True(vm.HasSavedEstimate);
    }

    [Fact] public async Task Save_WithOnlySomeFields_LeavesTheRestEmpty()
    {
        var vm = Setup();
        vm.SelectGenderCommand.Execute("2");
        vm.HeightCmText = "170";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.True(vm.IsDone, vm.Error);
        using var db = _db.CreateDbContext();
        var p = db.UserProfiles.Single(x => x.UserId == _me);
        Assert.Null(p.Sex);   // prefer not to say
        Assert.Equal(170.0, p.HeightCm);
        Assert.Null(p.HomeCityId);
        Assert.Empty(db.WeightLogs);
        Assert.Equal("Prefer not to say", vm.Summary[0].Value);
        Assert.Equal("Not set", vm.Summary.Single(r => r.Label == "Weight").Value);
        Assert.False(vm.HasSavedEstimate);
    }

    [Fact] public async Task UseAsGoal_SetsTheDailyCalorieGoalToMaintenance()
    {
        var vm = Setup();
        FillAll(vm);
        await vm.SaveCommand.ExecuteAsync(null);

        await vm.UseAsGoalCommand.ExecuteAsync(null);

        Assert.True(vm.IsGoalSet, vm.Error);
        using var db = _db.CreateDbContext();
        var age = BodyMetrics.AgeOn(new DateOnly(2000, 8, 24), AppClock.Today);
        Assert.Equal(BodyMetrics.Maintenance(Sex.Male, 80, 180, age, ActivityLevel.LessActive),
            db.Users.Single(u => u.Id == _me).DailyCalorieGoal);
    }

    [Fact] public void SignOut_ClearsTheNewAccountFlag()
    {
        Assert.True(_session.IsNewAccount);
        _session.SignOut();
        Assert.False(_session.IsNewAccount);
    }
}
