using GymApp.Core;
using GymApp.Services;

namespace GymApp.Tests;

/// <summary>The goal sheet's Recommended and Custom modes (Phase 27) and how recommended targets follow the trend weight.</summary>
public class CalorieMacroPlanServiceTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 14);

    private readonly TestDb _db = new();
    private readonly CalorieService _svc;
    private readonly ProfileService _profiles;
    private readonly int _alice;

    public CalorieMacroPlanServiceTests()
    {
        _svc = new CalorieService(_db, new PointsService(_db));
        _profiles = new ProfileService(_db);
        _alice = _db.AddUser("alice");
    }

    public void Dispose() => _db.Dispose();

    // Male, 30, 180 cm, 80 kg, Active
    private async Task FillProfileAsync(ActivityLevel level = ActivityLevel.Active)
    {
        await _profiles.SaveAsync(_alice, new ProfileInput(Sex.Male, new DateOnly(1996, 1, 1), 180, level, UnitSystem.Metric), Today);
        await _profiles.LogWeightAsync(_alice, 80, Today);
    }

    private async Task<CalorieOverview> GetAsync(DateOnly? day = null) => (await _svc.GetAsync(_alice, day ?? Today))!;

    [Fact] public async Task UsingTheRecommendation_StoresItsGramsAndCalories_AndTheWeightItCameFrom()
    {
        await FillProfileAsync();
        Assert.True((await _svc.UseRecommendedAsync(_alice, GoalType.Lose, Today)).Success);

        var g = (await GetAsync()).Goal;
        Assert.Equal(MacroMode.Recommended, g.MacroMode);
        Assert.Equal(GoalType.Lose, g.GoalType);
        // 2.2 / 2.5 / 0.7 g per kg would be 2,008 kcal, under the safe minimum of 2,070 (25% under 2,760): carbs make up the rest
        Assert.Equal((176, 216, 56), (g.ProteinGoalGrams, g.CarbsGoalGrams, g.FatGoalGrams));
        Assert.Equal(2072, g.Goal);
        Assert.Equal(80, g.MacroInputs!.WeightKg);
        Assert.True(g.MacroInputs.For(GoalType.Lose).RaisedToMinimum);
        Assert.Equal(2072, g.MacroInputs.For(GoalType.Lose).Kcal);
    }

    [Fact] public async Task WithoutBodyData_ThereIsNoRecommendation_AndItSaysWhy()
    {
        var r = await _svc.UseRecommendedAsync(_alice, GoalType.Maintain, Today);
        Assert.False(r.Success);
        Assert.Contains("gender", r.Error);
        Assert.Null((await GetAsync()).Goal.MacroInputs);
    }

    [Fact] public async Task ADaysWeighIn_DoesNotMoveTheTargets_ButAKiloOnTheTrendDoes()
    {
        await FillProfileAsync();
        await _svc.UseRecommendedAsync(_alice, GoalType.Maintain, Today);
        var before = (await GetAsync()).Goal.Goal;

        // a heavy day: the 2-week average is 80.75, under a kilo from 80
        await _profiles.LogWeightAsync(_alice, 81.5, Today.AddDays(1));
        var o = await GetAsync(Today.AddDays(1));
        Assert.Equal(before, o.Goal.Goal);
        Assert.Null(o.TargetsUpdate);

        // a week of lighter weigh-ins: the average drops to 78.9, 1.1 kg under
        for (var d = 2; d <= 8; d++) await _profiles.LogWeightAsync(_alice, 78.4, Today.AddDays(d));
        o = await GetAsync(Today.AddDays(8));
        Assert.NotNull(o.TargetsUpdate);
        Assert.Equal(before, o.TargetsUpdate!.OldKcal);
        Assert.Equal(o.Goal.Goal, o.TargetsUpdate.NewKcal);
        Assert.True(o.TargetsUpdate.NewKcal < before);
        Assert.Equal(-1.1, o.TargetsUpdate.WeightChangeKg, 1);

        // shown once: the next read changes nothing
        Assert.Null((await GetAsync(Today.AddDays(8))).TargetsUpdate);
    }

    [Fact] public async Task ABodyDataChange_UpdatesRecommendedTargetsRightAway_WithoutANotice()
    {
        await FillProfileAsync();
        await _svc.UseRecommendedAsync(_alice, GoalType.Maintain, Today);
        var carbs = (await GetAsync()).Goal.CarbsGoalGrams;

        await _profiles.SaveAsync(_alice, new ProfileInput(Sex.Male, new DateOnly(1996, 1, 1), 180, ActivityLevel.VeryActive, UnitSystem.Metric), Today);
        var o = await GetAsync();
        Assert.Equal(carbs + 40, o.Goal.CarbsGoalGrams);   // +0.5 g/kg on 80 kg
        Assert.Null(o.TargetsUpdate);
    }

    [Fact] public async Task CustomMacros_SetTheGoalToWhatTheyAddUpTo_AndNeverFollowTheWeight()
    {
        await FillProfileAsync();
        Assert.True((await _svc.SetCustomMacrosAsync(_alice, GoalType.Lose, 170, 250, 70, Today)).Success);
        var g = (await GetAsync()).Goal;
        Assert.Equal(MacroMode.Custom, g.MacroMode);
        Assert.Equal(2310, g.Goal);
        Assert.Equal((170, 250, 70), (g.ProteinGoalGrams, g.CarbsGoalGrams, g.FatGoalGrams));

        for (var d = 1; d <= 8; d++) await _profiles.LogWeightAsync(_alice, 75, Today.AddDays(d));
        var o = await GetAsync(Today.AddDays(8));
        Assert.Equal(2310, o.Goal.Goal);
        Assert.Null(o.TargetsUpdate);
    }

    [Fact] public async Task CustomMacros_NeedAllThree_AndASafeTotal()
    {
        await FillProfileAsync();
        Assert.Contains("fat", (await _svc.SetCustomMacrosAsync(_alice, GoalType.Lose, 150, 150, null, Today)).Error);
        Assert.Contains("between 0 and 1,000", (await _svc.SetCustomMacrosAsync(_alice, GoalType.Lose, 1200, 0, 0, Today)).Error);

        var tooLow = await _svc.SetCustomMacrosAsync(_alice, GoalType.Lose, 100, 100, 20, Today);   // 980 kcal
        Assert.False(tooLow.Success);
        Assert.StartsWith("Your macros add up to 980 kcal.", tooLow.Error);

        Assert.True((await _svc.SetCustomMacrosAsync(_alice, GoalType.Lose, 180, 0, 160, Today)).Success);   // keto: 0 carbs is fine
    }

    [Fact] public async Task BeforeAModeIsPicked_TheOldGoalStaysAsItWas()
    {
        await FillProfileAsync();
        await _svc.SetCustomGoalAsync(_alice, 2500, Today);
        for (var d = 1; d <= 8; d++) await _profiles.LogWeightAsync(_alice, 75, Today.AddDays(d));
        var g = (await GetAsync(Today.AddDays(8))).Goal;
        Assert.Null(g.MacroMode);
        Assert.Equal(2500, g.Goal);
        Assert.Null(g.ProteinGoalGrams);
        Assert.NotNull(g.MacroInputs);   // the sheet can still show what it would recommend
    }
}
