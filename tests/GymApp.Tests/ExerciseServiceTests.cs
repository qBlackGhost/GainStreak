using GymApp.Data.Models;
using GymApp.Services;

namespace GymApp.Tests;

public class ExerciseServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly ExerciseService _svc;
    private readonly int _alice;
    private readonly int _bob;

    public ExerciseServiceTests()
    {
        _svc = new ExerciseService(_db);
        _alice = _db.AddUser("alice");
        _bob = _db.AddUser("bob");
    }

    public void Dispose() => _db.Dispose();

    [Fact] public async Task Available_ContainsBuiltIns_AndOnlyYourOwnCustom()
    {
        await _svc.CreateAsync(_alice, "Alice Special", MuscleGroup.Chest);
        await _svc.CreateAsync(_bob, "Bob Special", MuscleGroup.Chest);

        var names = (await _svc.GetAvailableAsync(_alice)).Select(e => e.Name).ToList();
        Assert.Contains("Bench Press", names);
        Assert.Contains("Alice Special", names);
        Assert.DoesNotContain("Bob Special", names);
    }

    [Fact] public async Task Available_IsGroupedByMuscleGroupInEnumOrder()
    {
        var groups = (await _svc.GetAvailableAsync(_alice)).Select(e => e.MuscleGroup).ToList();
        Assert.Equal(groups.OrderBy(g => g).ToList(), groups);
    }

    [Fact] public async Task Search_MatchesPartOfTheName_IgnoringCase()
    {
        var found = await _svc.GetAvailableAsync(_alice, "bench");
        Assert.NotEmpty(found);
        Assert.All(found, e => Assert.Contains("bench", e.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact] public async Task Create_Works_AndTrimsTheName()
    {
        var r = await _svc.CreateAsync(_alice, "  Cable Pullover  ", MuscleGroup.Back);
        Assert.True(r.Success);
        Assert.Equal("Cable Pullover", r.Exercise!.Name);
        Assert.Equal(_alice, r.Exercise.OwnerUserId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_RejectsBlankNames(string name) =>
        Assert.False((await _svc.CreateAsync(_alice, name, MuscleGroup.Back)).Success);

    [Fact] public async Task Create_RejectsTooLongNames() =>
        Assert.False((await _svc.CreateAsync(_alice, new string('x', 61), MuscleGroup.Back)).Success);

    [Fact] public async Task Create_RejectsANameThatMatchesABuiltIn_IgnoringCase() =>
        Assert.False((await _svc.CreateAsync(_alice, "bench press", MuscleGroup.Chest)).Success);

    [Fact] public async Task Create_RejectsADuplicateOfYourOwn()
    {
        Assert.True((await _svc.CreateAsync(_alice, "Cable Pullover", MuscleGroup.Back)).Success);
        Assert.False((await _svc.CreateAsync(_alice, "CABLE PULLOVER", MuscleGroup.Back)).Success);
    }

    [Fact] public async Task Create_AllowsTheSameNameForADifferentUser()
    {
        Assert.True((await _svc.CreateAsync(_alice, "Cable Pullover", MuscleGroup.Back)).Success);
        Assert.True((await _svc.CreateAsync(_bob, "Cable Pullover", MuscleGroup.Back)).Success);
    }

    [Fact] public async Task Create_EnforcesTheCap()
    {
        for (var i = 0; i < ExerciseService.MaxCustomExercises; i++)
            Assert.True((await _svc.CreateAsync(_alice, $"Custom {i}", MuscleGroup.Core)).Success);

        Assert.False((await _svc.CreateAsync(_alice, "One too many", MuscleGroup.Core)).Success);
        Assert.True((await _svc.CreateAsync(_bob, "Bob is fine", MuscleGroup.Core)).Success);
    }

    [Fact] public async Task Update_RenamesAndRegroups()
    {
        var created = (await _svc.CreateAsync(_alice, "Cable Pullover", MuscleGroup.Back)).Exercise!;
        var r = await _svc.UpdateAsync(_alice, created.Id, "Straight Arm Pulldown", MuscleGroup.Back);
        Assert.True(r.Success);
        Assert.Equal("Straight Arm Pulldown", r.Exercise!.Name);
    }

    [Fact] public async Task Update_CannotRenameToABuiltInName()
    {
        var created = (await _svc.CreateAsync(_alice, "Cable Pullover", MuscleGroup.Back)).Exercise!;
        Assert.False((await _svc.UpdateAsync(_alice, created.Id, "Deadlift", MuscleGroup.Back)).Success);
    }

    [Fact] public async Task Update_CanKeepItsOwnName()
    {
        var created = (await _svc.CreateAsync(_alice, "Cable Pullover", MuscleGroup.Back)).Exercise!;
        Assert.True((await _svc.UpdateAsync(_alice, created.Id, "Cable Pullover", MuscleGroup.Shoulders)).Success);
    }

    [Fact] public async Task Update_RefusesBuiltInsAndOtherUsersExercises()
    {
        var bobs = (await _svc.CreateAsync(_bob, "Bob Special", MuscleGroup.Core)).Exercise!;
        Assert.False((await _svc.UpdateAsync(_alice, bobs.Id, "Hijacked", MuscleGroup.Core)).Success);
        Assert.False((await _svc.UpdateAsync(_alice, 1, "Hijacked", MuscleGroup.Chest)).Success);   // 1 = Bench Press
    }

    [Fact] public async Task Delete_RemovesYourOwn()
    {
        var created = (await _svc.CreateAsync(_alice, "Cable Pullover", MuscleGroup.Back)).Exercise!;
        Assert.True(await _svc.DeleteAsync(_alice, created.Id));
        Assert.Empty(await _svc.GetCustomAsync(_alice));
    }

    [Fact] public async Task Delete_RefusesBuiltInsAndOtherUsersExercises()
    {
        var bobs = (await _svc.CreateAsync(_bob, "Bob Special", MuscleGroup.Core)).Exercise!;
        Assert.False(await _svc.DeleteAsync(_alice, bobs.Id));
        Assert.False(await _svc.DeleteAsync(_alice, 1));
        Assert.Contains(await _svc.GetAvailableAsync(_alice), e => e.Id == 1);
    }

    [Fact] public async Task Usage_CountsDayListsAndSets_AndDeleteRemovesThem()
    {
        var ex = (await _svc.CreateAsync(_alice, "Cable Pullover", MuscleGroup.Back)).Exercise!;
        await using (var db = _db.CreateDbContext())
        {
            db.UserDayExercises.Add(new UserDayExercise { UserId = _alice, SplitDayId = 1, ExerciseId = ex.Id });
            db.SetLogs.Add(new SetLog { UserId = _alice, ExerciseId = ex.Id, Date = new DateOnly(2026, 10, 13), SetNumber = 1, Reps = 10, WeightKg = 40 });
            db.SetLogs.Add(new SetLog { UserId = _alice, ExerciseId = ex.Id, Date = new DateOnly(2026, 10, 13), SetNumber = 2, Reps = 8, WeightKg = 42.5 });
            await db.SaveChangesAsync();
        }

        Assert.Equal(new ExerciseUsage(1, 2), await _svc.GetUsageAsync(_alice, ex.Id));
        Assert.Equal(new ExerciseUsage(1, 2), (await _svc.GetUsagesAsync(_alice))[ex.Id]);

        await _svc.DeleteAsync(_alice, ex.Id);
        Assert.Equal(new ExerciseUsage(0, 0), await _svc.GetUsageAsync(_alice, ex.Id));
    }
}