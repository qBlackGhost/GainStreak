using GymApp.Core;
using GymApp.Services;

namespace GymApp.Tests;

public class CalorieServiceTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 14);

    private readonly TestDb _db = new();
    private readonly CalorieService _svc;
    private readonly ProfileService _profiles;
    private readonly int _alice;

    public CalorieServiceTests()
    {
        _svc = new CalorieService(_db);
        _profiles = new ProfileService(_db);
        _alice = _db.AddUser("alice");
    }

    public void Dispose() => _db.Dispose();

    // Male, 30, 180 cm, 80 kg, Active: maintenance 2,760 kcal
    private async Task FillProfileAsync()
    {
        await _profiles.SaveAsync(_alice, new ProfileInput(Sex.Male, new DateOnly(1996, 1, 1), 180, ActivityLevel.Active, UnitSystem.Metric), Today);
        await _profiles.LogWeightAsync(_alice, 80, Today);
    }

    private async Task<CalorieGoalInfo> GoalAsync() => (await _svc.GetAsync(_alice, Today))!.Goal;

    [Fact] public async Task NoProfile_NoSuggestion_ButSaysWhy()
    {
        var g = await GoalAsync();
        Assert.Equal(GoalType.Maintain, g.GoalType);
        Assert.Null(g.Goal);
        Assert.Null(g.Suggestion);
        Assert.Contains("sex", g.SuggestionReason);
        Assert.False((await _svc.AcceptSuggestionAsync(_alice, Today)).Success);
    }

    [Fact] public async Task Accept_StoresTheSuggestion()
    {
        await FillProfileAsync();
        Assert.Equal(2760, (await GoalAsync()).Suggestion!.Kcal);

        Assert.True((await _svc.AcceptSuggestionAsync(_alice, Today)).Success);
        var g = await GoalAsync();
        Assert.Equal(2760, g.Goal);
        Assert.False(g.SuggestionChanged);
    }

    [Fact] public async Task ChangingGoalType_AsksBeforeOverwriting()
    {
        await FillProfileAsync();
        await _svc.AcceptSuggestionAsync(_alice, Today);

        await _svc.SetGoalTypeAsync(_alice, GoalType.Lose);
        var g = await GoalAsync();
        Assert.Equal(2760, g.Goal);                 // not overwritten
        Assert.Equal(2260, g.Suggestion!.Kcal);
        Assert.True(g.SuggestionChanged);

        await _svc.AcceptSuggestionAsync(_alice, Today);
        g = await GoalAsync();
        Assert.Equal(2260, g.Goal);
        Assert.False(g.SuggestionChanged);
    }

    [Fact] public async Task Keep_StopsAsking_UntilTheSuggestionMovesAgain()
    {
        await FillProfileAsync();
        await _svc.AcceptSuggestionAsync(_alice, Today);
        await _svc.SetGoalTypeAsync(_alice, GoalType.Gain);

        await _svc.KeepGoalAsync(_alice, Today);
        var g = await GoalAsync();
        Assert.Equal(2760, g.Goal);
        Assert.False(g.SuggestionChanged);

        await _profiles.LogWeightAsync(_alice, 90, Today);   // new weight, new suggestion
        Assert.True((await GoalAsync()).SuggestionChanged);
    }

    [Fact] public async Task CustomGoal_IsKept_AndRespectsTheLimits()
    {
        await FillProfileAsync();
        Assert.False((await _svc.SetCustomGoalAsync(_alice, 1400, Today)).Success);   // under the male floor
        Assert.False((await _svc.SetCustomGoalAsync(_alice, 2000, Today)).Success);   // over 25 percent deficit
        Assert.True((await _svc.SetCustomGoalAsync(_alice, 2500, Today)).Success);

        var g = await GoalAsync();
        Assert.Equal(2500, g.Goal);
        Assert.False(g.SuggestionChanged);   // they already saw 2,760 and chose their own
    }

    [Fact] public async Task UnderEighteen_CanOnlySetTheirOwnGoal()
    {
        await _profiles.SaveAsync(_alice, new ProfileInput(Sex.Female, new DateOnly(2010, 1, 1), 160, ActivityLevel.Active, UnitSystem.Metric), Today);
        await _profiles.LogWeightAsync(_alice, 55, Today);

        var g = await GoalAsync();
        Assert.Null(g.Suggestion);
        Assert.Contains("18", g.SuggestionReason);
        Assert.False((await _svc.AcceptSuggestionAsync(_alice, Today)).Success);
        Assert.True((await _svc.SetCustomGoalAsync(_alice, 2000, Today)).Success);
        Assert.False((await _svc.SetCustomGoalAsync(_alice, 1100, Today)).Success);
    }

    [Fact] public async Task LogToday_SnapshotsTheGoal_AndJudgesTheTarget()
    {
        await FillProfileAsync();
        await _svc.SetGoalTypeAsync(_alice, GoalType.Lose);
        await _svc.AcceptSuggestionAsync(_alice, Today);   // 2,260

        Assert.True((await _svc.LogTodayAsync(_alice, 2400, Today)).Success);
        Assert.True((await _svc.LogTodayAsync(_alice, 2200, Today)).Success);   // replaces

        var o = (await _svc.GetAsync(_alice, Today))!;
        Assert.Single(o.Recent);
        Assert.Equal(2200, o.Today!.Consumed);
        Assert.Equal(2260, o.Today.Goal);
        Assert.Equal(GoalType.Lose, o.Today.GoalType);
        Assert.True(o.Today.Hit);

        // A later goal change does not rewrite how that day is judged
        await _svc.SetGoalTypeAsync(_alice, GoalType.Gain);
        await _svc.AcceptSuggestionAsync(_alice, Today);
        var day = (await _svc.GetAsync(_alice, Today.AddDays(1)))!.Recent.Single();
        Assert.Equal(2260, day.Goal);
        Assert.True(day.Hit);
    }

    [Fact] public async Task LogToday_WithoutAGoal_HasNoVerdict()
    {
        Assert.True((await _svc.LogTodayAsync(_alice, 2000, Today)).Success);
        var day = (await _svc.GetAsync(_alice, Today))!.Today!;
        Assert.Null(day.Goal);
        Assert.Null(day.GoalType);
        Assert.Null(day.Hit);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(15001)]
    public async Task LogToday_RejectsOutOfRange(int kcal) =>
        Assert.False((await _svc.LogTodayAsync(_alice, kcal, Today)).Success);

    [Fact] public async Task Recent_IsNewestFirst_AndTodayIsOnlyToday()
    {
        await _svc.LogTodayAsync(_alice, 1900, Today.AddDays(-2));
        await _svc.LogTodayAsync(_alice, 2100, Today.AddDays(-1));

        var o = (await _svc.GetAsync(_alice, Today))!;
        Assert.Null(o.Today);
        Assert.Equal(new[] { 2100, 1900 }, o.Recent.Select(d => d.Consumed));
    }
}
