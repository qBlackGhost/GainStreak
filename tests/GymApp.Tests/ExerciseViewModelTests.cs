using GymApp.Core;
using GymApp.App.ViewModels;
using GymApp.Services;

namespace GymApp.Tests;

/// <summary>The Total / Per side weight toggle on the exercise screen.</summary>
public class ExerciseViewModelTests : IDisposable
{
    private const int PplPush = 1, DumbbellBench = 3, Deadlift = 12;

    private readonly TestDb _db = TestDb.OnDisk();
    private readonly SessionService _session = new();
    private readonly SetService _sets;
    private readonly DayExerciseService _days;
    private readonly int _alice;

    public ExerciseViewModelTests()
    {
        _alice = _db.AddUser("alice");
        using (var db = _db.CreateDbContext()) _session.SignIn(db.Users.Single(u => u.Id == _alice));
        _sets = new SetService(_db);
        _days = new DayExerciseService(_db);
    }

    public void Dispose() => _db.Dispose();

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    private async Task<ExerciseViewModel> Open(int exerciseId)
    {
        Assert.True((await _days.AddAsync(_alice, PplPush, exerciseId)).Success);
        var vm = new ExerciseViewModel(_sets, _days, _session, () => null!);
        vm.Load(new MenuDay(PplPush, "Push", "", 1, true, []), "PPL", exerciseId);
        await Until(() => vm.Rows.Count > 0);
        return vm;
    }

    [Fact] public async Task OnlyBarExercises_OfferTheToggle()
    {
        Assert.True((await Open(Deadlift)).UsesBar);
        Assert.False((await Open(DumbbellBench)).UsesBar);
    }

    [Fact] public async Task PerSide_SavesTheTotal()
    {
        var vm = await Open(Deadlift);
        vm.SetPerSideModeCommand.Execute(null);
        vm.BarText = "20";
        vm.Rows[0].WeightText = "60";
        vm.Rows[0].RepsText = "5";

        Assert.Equal("140 kg", vm.Rows[0].TotalHint);
        Assert.Equal("(60 × 2 + 20 bar)", vm.Rows[0].TotalDetail);
        Assert.Equal("KG / SIDE", vm.WeightHeader);

        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(140, Assert.Single(await _sets.GetDayAsync(_alice, Deadlift)).WeightKg);
        Assert.Equal("60", vm.Rows[0].WeightText);   // the saved total shows per side again
    }

    [Fact] public async Task NoBar_JustDoublesTheSide()
    {
        var vm = await Open(Deadlift);
        vm.SetPerSideModeCommand.Execute(null);
        vm.Rows[0].WeightText = "50";
        Assert.Equal("100 kg", vm.Rows[0].TotalHint);
    }

    [Fact] public async Task Switching_KeepsWhatYouTypedMeaningTheSameWeight()
    {
        var vm = await Open(Deadlift);
        vm.Rows[0].WeightText = "140";
        vm.BarText = "20";   // only counts per side

        vm.SetPerSideModeCommand.Execute(null);
        Assert.Equal("60", vm.Rows[0].WeightText);

        vm.SetTotalModeCommand.Execute(null);
        Assert.Equal("140", vm.Rows[0].WeightText);
        Assert.False(vm.Rows[0].HasTotalHint);
    }

    [Fact] public async Task TheModeAndBar_AreRememberedForTheExercise()
    {
        var vm = await Open(Deadlift);
        vm.SetPerSideModeCommand.Execute(null);
        vm.BarText = "15";
        await Until(() => _sets.GetBarSettingAsync(_alice, Deadlift).Result == new BarSetting(true, 15));

        await _sets.SaveDayAsync(_alice, Deadlift, [new SetInput(SetKind.Working, 5, 115)]);
        var again = new ExerciseViewModel(_sets, _days, _session, () => null!);
        again.Load(new MenuDay(PplPush, "Push", "", 1, true, []), "PPL", Deadlift);
        await Until(() => again.Rows.Count > 0);

        Assert.True(again.IsPerSide);
        Assert.Equal("15", again.BarText);
        Assert.Equal("50", again.Rows[0].WeightText);
    }

    [Fact] public async Task ABadBar_BlocksTheSave()
    {
        var vm = await Open(Deadlift);
        vm.SetPerSideModeCommand.Execute(null);
        vm.BarText = "abc";
        vm.Rows[0].WeightText = "60";
        vm.Rows[0].RepsText = "5";

        Assert.NotNull(vm.BarError);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(vm.Error);
        Assert.Empty(await _sets.GetDayAsync(_alice, Deadlift));
    }
}
