using GymApp.Data.Models;
using GymApp.Services;

namespace GymApp.Tests;

public class DayExerciseServiceTests : IDisposable
{
    private const int PplPush = 1, PplPull = 2, PplUlPush = 6;

    private readonly TestDb _db = new();
    private readonly DayExerciseService _svc;
    private readonly int _alice;
    private readonly int _bob;

    public DayExerciseServiceTests()
    {
        _svc = new DayExerciseService(_db);
        _alice = _db.AddUser("alice");
        _bob = _db.AddUser("bob");
    }

    public void Dispose() => _db.Dispose();

    private async Task Fill(int userId, int day, params int[] exerciseIds)
    {
        foreach (var id in exerciseIds)
            Assert.True((await _svc.AddAsync(userId, day, id)).Success);
    }

    private async Task<int[]> Ids(int userId, int day) =>
        (await _svc.GetListAsync(userId, day)).Select(x => x.ExerciseId).ToArray();

    [Fact] public async Task Add_AppendsInOrder()
    {
        await Fill(_alice, PplPush, 1, 2, 3);
        var list = await _svc.GetListAsync(_alice, PplPush);
        Assert.Equal(new[] { 1, 2, 3 }, list.Select(x => x.ExerciseId));
        Assert.Equal(new[] { 1, 2, 3 }, list.Select(x => x.Order));
        Assert.NotEmpty(list[0].Exercise.Name);
    }

    [Fact] public async Task Add_RejectsADuplicate()
    {
        await Fill(_alice, PplPush, 1);
        Assert.False((await _svc.AddAsync(_alice, PplPush, 1)).Success);
    }

    [Fact] public async Task Add_RejectsAnUnknownExercise_AndAnotherUsersCustomOne()
    {
        Assert.False((await _svc.AddAsync(_alice, PplPush, 99999)).Success);

        var bobs = (await new ExerciseService(_db).CreateAsync(_bob, "Bob Special", MuscleGroup.Chest)).Exercise!;
        Assert.False((await _svc.AddAsync(_alice, PplPush, bobs.Id)).Success);
        Assert.True((await _svc.AddAsync(_bob, PplPush, bobs.Id)).Success);
    }

    [Fact] public async Task Add_RejectsADayFromAnotherUsersCustomSplit()
    {
        int bobsDay;
        await using (var db = _db.CreateDbContext())
        {
            var split = new WorkoutSplit { Name = "Bob's split", OwnerUserId = _bob };
            split.Days.Add(new SplitDay { Name = "Chest", Order = 1, Weekday = DayOfWeek.Monday });
            db.WorkoutSplits.Add(split);
            await db.SaveChangesAsync();
            bobsDay = split.Days[0].Id;
        }

        Assert.False((await _svc.AddAsync(_alice, bobsDay, 1)).Success);
        Assert.True((await _svc.AddAsync(_bob, bobsDay, 1)).Success);
    }

    [Theory]
    [InlineData(-1, 3)]
    [InlineData(11, 3)]
    [InlineData(0, 0)]
    [InlineData(0, 16)]
    public async Task Add_RejectsInvalidSetCounts(int warmup, int working) =>
        Assert.False((await _svc.AddAsync(_alice, PplPush, 1, warmup, working)).Success);

    [Fact] public async Task Add_EnforcesThePerDayCap()
    {
        await Fill(_alice, PplPush, Enumerable.Range(1, DayExerciseService.MaxExercisesPerDay).ToArray());
        Assert.False((await _svc.AddAsync(_alice, PplPush, 30)).Success);
    }

    [Fact] public async Task Remove_RenumbersTheRest()
    {
        await Fill(_alice, PplPush, 1, 2, 3);
        Assert.True(await _svc.RemoveAsync(_alice, PplPush, 2));

        var list = await _svc.GetListAsync(_alice, PplPush);
        Assert.Equal(new[] { 1, 3 }, list.Select(x => x.ExerciseId));
        Assert.Equal(new[] { 1, 2 }, list.Select(x => x.Order));
        Assert.False(await _svc.RemoveAsync(_alice, PplPush, 2));
    }

    [Fact] public async Task Remove_KeepsLoggedSets()
    {
        await Fill(_alice, PplPush, 1);
        await using (var db = _db.CreateDbContext())
        {
            db.SetLogs.Add(new SetLog { UserId = _alice, ExerciseId = 1, Date = new DateOnly(2026, 10, 13), SetNumber = 1, Reps = 10, WeightKg = 60 });
            await db.SaveChangesAsync();
        }

        await _svc.RemoveAsync(_alice, PplPush, 1);

        await using var check = _db.CreateDbContext();
        Assert.Single(check.SetLogs.Where(s => s.UserId == _alice));
    }

    [Fact] public async Task Move_SwapsNeighbours_AndStopsAtTheEdges()
    {
        await Fill(_alice, PplPush, 1, 2, 3);

        Assert.True(await _svc.MoveAsync(_alice, PplPush, 3, -1));
        Assert.Equal(new[] { 1, 3, 2 }, await Ids(_alice, PplPush));

        Assert.True(await _svc.MoveAsync(_alice, PplPush, 1, +1));
        Assert.Equal(new[] { 3, 1, 2 }, await Ids(_alice, PplPush));

        Assert.False(await _svc.MoveAsync(_alice, PplPush, 3, -1));   // already first
        Assert.False(await _svc.MoveAsync(_alice, PplPush, 2, +1));   // already last
    }

    [Fact] public async Task MoveTo_InsertsAtTheIndex_AndClampsIt()
    {
        await Fill(_alice, PplPush, 1, 2, 3, 4);

        Assert.True(await _svc.MoveToAsync(_alice, PplPush, 4, 1));
        Assert.Equal(new[] { 1, 4, 2, 3 }, await Ids(_alice, PplPush));

        Assert.True(await _svc.MoveToAsync(_alice, PplPush, 1, 99));   // past the end: last
        Assert.Equal(new[] { 4, 2, 3, 1 }, await Ids(_alice, PplPush));

        Assert.True(await _svc.MoveToAsync(_alice, PplPush, 3, -5));   // before the start: first
        Assert.Equal(new[] { 3, 4, 2, 1 }, await Ids(_alice, PplPush));

        Assert.False(await _svc.MoveToAsync(_alice, PplPush, 9, 0));   // not in the list
    }

    [Fact] public async Task SetPlannedSets_UpdatesTheCounts()
    {
        await Fill(_alice, PplPush, 1);
        Assert.True((await _svc.SetPlannedSetsAsync(_alice, PplPush, 1, 2, 4)).Success);

        var row = (await _svc.GetListAsync(_alice, PplPush)).Single();
        Assert.Equal(2, row.WarmupSets);
        Assert.Equal(4, row.WorkingSets);
        Assert.False((await _svc.SetPlannedSetsAsync(_alice, PplPush, 2, 1, 3)).Success);   // not in the list
    }

    [Fact] public async Task CopySources_ListsOtherDaysWithExercises()
    {
        await Fill(_alice, PplPush, 1, 2);
        await Fill(_alice, PplPull, 7);
        await Fill(_bob, PplUlPush, 1);

        var sources = await _svc.GetCopySourcesAsync(_alice, PplUlPush);
        Assert.Equal(new[] { PplPush, PplPull }, sources.Select(s => s.SplitDayId));
        Assert.Equal(new[] { 2, 1 }, sources.Select(s => s.ExerciseCount));
        Assert.All(sources, s => Assert.Equal("Push / Pull / Legs", s.SplitName));

        Assert.DoesNotContain(await _svc.GetCopySourcesAsync(_alice, PplPush), s => s.SplitDayId == PplPush);
    }

    [Fact] public async Task Copy_Append_AddsTheMissingOnes_AndSkipsDuplicates()
    {
        await Fill(_alice, PplPush, 1, 2, 3);
        await _svc.SetPlannedSetsAsync(_alice, PplPush, 1, 2, 4);
        await Fill(_alice, PplUlPush, 2);

        var r = await _svc.CopyAsync(_alice, PplPush, PplUlPush, CopyMode.Append);

        Assert.True(r.Success);
        Assert.Equal(2, r.Added);
        Assert.Equal(1, r.Skipped);
        Assert.Equal(new[] { 2, 1, 3 }, await Ids(_alice, PplUlPush));

        var copied = (await _svc.GetListAsync(_alice, PplUlPush)).Single(x => x.ExerciseId == 1);
        Assert.Equal(2, copied.WarmupSets);   // planned sets come along
        Assert.Equal(4, copied.WorkingSets);
    }

    [Fact] public async Task Copy_Replace_SwapsTheWholeList()
    {
        await Fill(_alice, PplPush, 1, 2, 3);
        await Fill(_alice, PplUlPush, 22, 2);

        var r = await _svc.CopyAsync(_alice, PplPush, PplUlPush, CopyMode.Replace);

        Assert.True(r.Success);
        Assert.Equal(3, r.Added);
        Assert.Equal(0, r.Skipped);
        Assert.Equal(new[] { 1, 2, 3 }, await Ids(_alice, PplUlPush));
        Assert.Equal(new[] { 1, 2, 3 }, (await _svc.GetListAsync(_alice, PplUlPush)).Select(x => x.Order));
    }

    [Fact] public async Task Copy_LeavesTheSourceSetsAndPrAdjustmentsAlone()
    {
        await Fill(_alice, PplPush, 1);
        await using (var db = _db.CreateDbContext())
        {
            db.SetLogs.Add(new SetLog { UserId = _alice, ExerciseId = 1, Date = new DateOnly(2026, 10, 13), SetNumber = 1, Reps = 10, WeightKg = 60 });
            db.PrAdjustments.Add(new PrAdjustment { UserId = _alice, ExerciseId = 1, Date = new DateOnly(2026, 10, 13), WeightKg = 55, Reps = 10 });
            await db.SaveChangesAsync();
        }

        await _svc.CopyAsync(_alice, PplPush, PplUlPush, CopyMode.Replace);

        await using var check = _db.CreateDbContext();
        Assert.Single(check.SetLogs.Where(s => s.UserId == _alice));
        Assert.Single(check.PrAdjustments.Where(a => a.UserId == _alice));
        Assert.Equal(new[] { 1 }, await Ids(_alice, PplPush));   // source unchanged
    }

    [Fact] public async Task Copy_OnlyUsesYourOwnLists()
    {
        await Fill(_bob, PplPush, 1, 2);
        Assert.False((await _svc.CopyAsync(_alice, PplPush, PplUlPush, CopyMode.Append)).Success);   // alice has nothing there
    }

    [Fact] public async Task Copy_Fails_WhenThereIsNothingToCopy_OrTheSameDayIsPicked()
    {
        Assert.False((await _svc.CopyAsync(_alice, PplPush, PplUlPush, CopyMode.Append)).Success);

        await Fill(_alice, PplPush, 1);
        Assert.False((await _svc.CopyAsync(_alice, PplPush, PplPush, CopyMode.Append)).Success);
    }

    [Fact] public async Task Copy_OverTheCap_FailsAndChangesNothing()
    {
        await Fill(_alice, PplPush, Enumerable.Range(1, 15).ToArray());
        await Fill(_alice, PplUlPush, Enumerable.Range(11, 10).ToArray());   // 11..20, overlaps 11..15

        var before = await Ids(_alice, PplUlPush);
        // 10 existing + 10 new (1..10) = 20, which fits; add one more source exercise to push it over
        await Fill(_alice, PplPush, 30);
        var r = await _svc.CopyAsync(_alice, PplPush, PplUlPush, CopyMode.Append);

        Assert.False(r.Success);
        Assert.Equal(before, await Ids(_alice, PplUlPush));
    }
}