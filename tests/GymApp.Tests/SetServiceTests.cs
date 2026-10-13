using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;

namespace GymApp.Tests;

public class SetServiceTests : IDisposable
{
    private static readonly DateOnly Day0 = new(2026, 10, 5);
    private static DateOnly D(int n) => Day0.AddDays(n);

    private readonly TestDb _db = new();
    private readonly SetService _svc;
    private readonly int _alice;
    private readonly int _bob;

    public SetServiceTests()
    {
        _svc = new SetService(_db);
        _alice = _db.AddUser("alice");
        _bob = _db.AddUser("bob");
    }

    public void Dispose() => _db.Dispose();

    private static SetInput Work(double kg, int reps) => new(SetKind.Working, reps, kg);
    private static SetInput Warm(double kg, int reps) => new(SetKind.Warmup, reps, kg);

    private async Task Save(int userId, int exerciseId, int day, params SetInput[] sets) =>
        Assert.True((await _svc.SaveDayAsync(userId, exerciseId, sets, D(day))).Success);

    [Fact] public async Task Save_ThenGetDay_RoundTripsInOrder()
    {
        await Save(_alice, 1, 0, Warm(20, 10), Work(60, 10), Work(60, 8));

        var rows = await _svc.GetDayAsync(_alice, 1, D(0));
        Assert.Equal(new[] { 1, 2, 3 }, rows.Select(r => r.SetNumber));
        Assert.Equal(new[] { SetKind.Warmup, SetKind.Working, SetKind.Working }, rows.Select(r => r.Kind));
        Assert.Equal(new[] { 20.0, 60.0, 60.0 }, rows.Select(r => r.WeightKg));
        Assert.Equal(new[] { 10, 10, 8 }, rows.Select(r => r.Reps));
    }

    [Fact] public async Task Save_ReplacesTheDay_AndEmptyClearsIt()
    {
        await Save(_alice, 1, 0, Work(60, 10), Work(60, 8), Work(60, 6));
        await Save(_alice, 1, 0, Work(62.5, 5));
        Assert.Equal(62.5, Assert.Single(await _svc.GetDayAsync(_alice, 1, D(0))).WeightKg);

        await Save(_alice, 1, 0);
        Assert.Empty(await _svc.GetDayAsync(_alice, 1, D(0)));
    }

    [Fact] public async Task Save_RoundsTheWeightToTwoDecimals()
    {
        await Save(_alice, 1, 0, Work(62.504, 5));
        Assert.Equal(62.5, (await _svc.GetDayAsync(_alice, 1, D(0))).Single().WeightKg);
    }

    [Fact] public async Task Save_OnlyTouchesThatDayAndThatExercise()
    {
        await Save(_alice, 1, 0, Work(60, 10));
        await Save(_alice, 1, 1, Work(60, 12));
        await Save(_alice, 2, 1, Work(50, 10));

        await Save(_alice, 1, 1, Work(62.5, 8));

        Assert.Equal(60, (await _svc.GetDayAsync(_alice, 1, D(0))).Single().WeightKg);
        Assert.Equal(62.5, (await _svc.GetDayAsync(_alice, 1, D(1))).Single().WeightKg);
        Assert.Equal(50, (await _svc.GetDayAsync(_alice, 2, D(1))).Single().WeightKg);
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(101, 60)]
    [InlineData(10, -1)]
    [InlineData(10, 1000.01)]
    [InlineData(10, double.NaN)]
    public async Task Save_RejectsInvalidSets_AndKeepsTheOldOnes(int reps, double kg)
    {
        await Save(_alice, 1, 0, Work(60, 10));

        var r = await _svc.SaveDayAsync(_alice, 1, [Work(kg, reps)], D(0));

        Assert.False(r.Success);
        Assert.Equal(60, (await _svc.GetDayAsync(_alice, 1, D(0))).Single().WeightKg);
    }

    [Fact] public async Task Save_AllowsBodyweightSets() =>
        await Save(_alice, 7, 0, Work(0, 12));   // pull-ups with no extra weight

    [Fact] public async Task Save_RejectsTooManySets()
    {
        var sets = Enumerable.Range(0, SetService.MaxSetsPerExercisePerDay + 1).Select(_ => Work(60, 10)).ToArray();
        Assert.False((await _svc.SaveDayAsync(_alice, 1, sets, D(0))).Success);
    }

    [Fact] public async Task Save_RejectsAnUnknownExercise_AndAnotherUsersCustomOne()
    {
        Assert.False((await _svc.SaveDayAsync(_alice, 99999, [Work(60, 10)], D(0))).Success);

        var bobs = (await new ExerciseService(_db).CreateAsync(_bob, "Bob Special", MuscleGroup.Chest)).Exercise!;
        Assert.False((await _svc.SaveDayAsync(_alice, bobs.Id, [Work(60, 10)], D(0))).Success);
        Assert.True((await _svc.SaveDayAsync(_bob, bobs.Id, [Work(60, 10)], D(0))).Success);
    }

    [Fact] public async Task Save_NeverCreatesAWorkoutLog()
    {
        await Save(_alice, 1, 0, Work(60, 10));

        await using var db = _db.CreateDbContext();
        Assert.Empty(db.WorkoutLogs.Where(w => w.UserId == _alice));
    }

    [Fact] public async Task Users_DoNotSeeEachOthersSets()
    {
        await Save(_alice, 1, 0, Work(100, 5));
        await Save(_bob, 1, 0, Work(60, 10));

        Assert.Equal(100, (await _svc.GetHistoryAsync(_alice, 1)).Pr.Current!.WeightKg);
        Assert.Equal(60, (await _svc.GetHistoryAsync(_bob, 1)).Pr.Current!.WeightKg);
    }

    [Fact] public async Task History_FollowsTheWeightThenRepsRule()
    {
        await Save(_alice, 1, 0, Work(60, 10));
        await Save(_alice, 1, 7, Work(60, 12));

        var h = await _svc.GetHistoryAsync(_alice, 1);
        Assert.Equal(2, h.Pr.Entries.Count);
        Assert.Equal(12, h.Pr.Current!.Reps);

        await Save(_alice, 1, 14, Work(65, 5));
        Assert.Equal(65, (await _svc.GetHistoryAsync(_alice, 1)).Pr.Current!.WeightKg);
    }

    [Fact] public async Task History_IgnoresWarmups()
    {
        await Save(_alice, 1, 0, Warm(100, 5), Work(60, 10));
        Assert.Equal(60, (await _svc.GetHistoryAsync(_alice, 1)).Pr.Current!.WeightKg);
    }

    [Fact] public async Task PrBefore_IgnoresThatDayAndLater()
    {
        await Save(_alice, 1, 0, Work(60, 10));
        await Save(_alice, 1, 7, Work(62.5, 5));

        var h = await _svc.GetHistoryAsync(_alice, 1);
        Assert.Equal(62.5, h.Pr.Current!.WeightKg);
        Assert.Equal(60, h.PrBefore(D(7)).Current!.WeightKg);
        Assert.Null(h.PrBefore(D(0)).Current);
    }

    [Fact] public async Task LastSession_ReturnsTheMostRecentEarlierDay()
    {
        await Save(_alice, 1, 0, Work(60, 10));
        await Save(_alice, 1, 7, Work(62.5, 6), Work(62.5, 5));

        var last = await _svc.GetLastSessionAsync(_alice, 1, D(14));
        Assert.Equal(2, last.Count);
        Assert.All(last, s => Assert.Equal(62.5, s.WeightKg));

        Assert.Single(await _svc.GetLastSessionAsync(_alice, 1, D(7)));   // before day 7 -> day 0
        Assert.Empty(await _svc.GetLastSessionAsync(_alice, 1, D(0)));
    }

    [Fact] public async Task HasSetsToday_IsPerUserAndPerDay()
    {
        await Save(_alice, 1, 0, Work(60, 10));

        Assert.True(await _svc.HasSetsTodayAsync(_alice, D(0)));
        Assert.False(await _svc.HasSetsTodayAsync(_alice, D(1)));
        Assert.False(await _svc.HasSetsTodayAsync(_bob, D(0)));
    }

    [Fact] public async Task SetPr_LowersTheCurrentBest()
    {
        await Save(_alice, 1, 0, Work(60, 10));

        Assert.True((await _svc.SetPrAsync(_alice, 1, 55, 10, null, D(1))).Success);

        var current = (await _svc.GetHistoryAsync(_alice, 1)).Pr.Current!;
        Assert.Equal(PrEntryKind.Manual, current.Kind);
        Assert.Equal(55, current.WeightKg);
    }

    [Fact] public async Task SetPr_TwiceInADay_UpdatesTheSameRow()
    {
        await Save(_alice, 1, 0, Work(60, 10));
        await _svc.SetPrAsync(_alice, 1, 55, 10, null, D(1));
        await _svc.SetPrAsync(_alice, 1, 57.5, 8, 90, D(1));

        var h = await _svc.GetHistoryAsync(_alice, 1);
        Assert.Single(h.Adjustments);
        Assert.Equal(57.5, h.Pr.Current!.WeightKg);
        Assert.Equal(90, h.Pr.Current.OneRepMaxKg);
    }

    [Fact] public async Task SetPr_ThenLaterSetsAreComparedToIt()
    {
        await Save(_alice, 1, 0, Work(60, 10));
        await _svc.SetPrAsync(_alice, 1, 55, 10, null, D(1));
        await Save(_alice, 1, 7, Work(57.5, 8));

        Assert.Equal(57.5, (await _svc.GetHistoryAsync(_alice, 1)).Pr.Current!.WeightKg);
    }

    [Fact] public async Task RemovePrAdjustment_RestoresTheDerivedPr()
    {
        await Save(_alice, 1, 0, Work(60, 10));
        await _svc.SetPrAsync(_alice, 1, 55, 10, null, D(1));

        Assert.True(await _svc.RemovePrAdjustmentAsync(_alice, 1, D(1)));
        Assert.Equal(PrEntryKind.Baseline, (await _svc.GetHistoryAsync(_alice, 1)).Pr.Current!.Kind);
        Assert.False(await _svc.RemovePrAdjustmentAsync(_alice, 1, D(1)));
    }

    [Theory]
    [InlineData(55, 0, null)]      // reps too low
    [InlineData(1001, 5, null)]    // weight too high
    [InlineData(60, 10, 59.0)]     // a 1RM below the weight lifted
    [InlineData(60, 10, 2001.0)]   // an absurd 1RM
    public async Task SetPr_RejectsInvalidValues(double kg, int reps, double? oneRm) =>
        Assert.False((await _svc.SetPrAsync(_alice, 1, kg, reps, oneRm, D(0))).Success);

    [Fact] public async Task SetPr_CanSeedAFirstPr_ForAnExerciseWithNoSets() 
    {
        Assert.True((await _svc.SetPrAsync(_alice, 1, 80, 5, null, D(0))).Success);
        Assert.Equal(80, (await _svc.GetHistoryAsync(_alice, 1)).Pr.Current!.WeightKg);
    }

    [Fact] public async Task SetPr_RejectsAnotherUsersCustomExercise()
    {
        var bobs = (await new ExerciseService(_db).CreateAsync(_bob, "Bob Special", MuscleGroup.Chest)).Exercise!;
        Assert.False((await _svc.SetPrAsync(_alice, bobs.Id, 60, 10, null, D(0))).Success);
    }

    [Fact] public async Task DeletingACustomExercise_RemovesItsSetsAndAdjustments()
    {
        var exercises = new ExerciseService(_db);
        var ex = (await exercises.CreateAsync(_alice, "Cable Pullover", MuscleGroup.Back)).Exercise!;
        await Save(_alice, ex.Id, 0, Work(40, 10));
        await _svc.SetPrAsync(_alice, ex.Id, 35, 10, null, D(1));

        await exercises.DeleteAsync(_alice, ex.Id);

        var h = await _svc.GetHistoryAsync(_alice, ex.Id);
        Assert.Empty(h.Sets);
        Assert.Empty(h.Adjustments);
    }
    
    [Fact] public async Task SetPr_ThenAHeavierSetTheSameDay_IsComparedToIt()
    {
        await _svc.SetPrAsync(_alice, 1, 55, 10, null, D(1));
        await Save(_alice, 1, 1, Work(60, 10));

        var current = (await _svc.GetHistoryAsync(_alice, 1)).Pr.Current!;
        Assert.Equal(60, current.WeightKg);
        Assert.Equal(PrEntryKind.Pr, current.Kind);
    }
}