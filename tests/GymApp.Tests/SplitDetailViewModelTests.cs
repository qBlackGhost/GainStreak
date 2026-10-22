using GymApp.Contracts;
using GymApp.Data.Models;
using GymApp.Desktop.ViewModels;
using GymApp.Services;

namespace GymApp.Tests;

public class SplitDetailViewModelTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly SessionService _session = new();
    private readonly SplitService _splits;
    private readonly int _alice;

    private sealed class NoUsage : ISplitUsageService
    {
        public Task<SplitUsageResponse?> GetAsync(int userId) => Task.FromResult<SplitUsageResponse?>(null);
    }

    public SplitDetailViewModelTests()
    {
        _alice = _db.AddUser("alice");
        using (var db = _db.CreateDbContext()) _session.SignIn(db.Users.Single(u => u.Id == _alice));
        _splits = new SplitService(_db, new PointsService(_db));
    }

    public void Dispose() => _db.Dispose();

    private async Task<SplitDetailViewModel> Open()
    {
        var preset = (await _splits.GetPresetsAsync()).First(p => p.Variants.Count > 0);
        var vm = new SplitDetailViewModel(_splits, new DayExerciseService(_db), _session, new NoUsage(), () => null!);
        vm.Load(preset);
        return vm;
    }

    [Fact] public async Task NotInUse_OffersUseThisSplit()
    {
        var vm = await Open();
        Assert.Equal("Use this split", vm.UseLabel);
        Assert.True(vm.UseCommand.CanExecute(null));
        Assert.False(vm.IsInUse);
    }

    [Fact] public async Task Use_SavesAtOnce_ThenReadsInUse_AndCantBeClicked()
    {
        var vm = await Open();
        await vm.UseCommand.ExecuteAsync(null);

        Assert.True(vm.IsInUse);
        Assert.True(vm.JustSaved);
        Assert.Equal("In use", vm.UseLabel);
        Assert.False(vm.UseCommand.CanExecute(null));
        using var db = _db.CreateDbContext();
        Assert.NotNull(db.Users.Single(u => u.Id == _alice).SelectedSplitId);
    }

    [Fact] public async Task InUse_ChangingTheStartDay_ShowsSave_AndChangingBack_ShowsInUse()
    {
        var vm = await Open();
        await vm.UseCommand.ExecuteAsync(null);

        vm.StartDay = DayOfWeek.Wednesday;
        Assert.Equal("Save", vm.UseLabel);
        Assert.False(vm.JustSaved);
        Assert.True(vm.UseCommand.CanExecute(null));

        vm.StartDay = DayOfWeek.Monday;
        Assert.Equal("In use", vm.UseLabel);
        Assert.False(vm.UseCommand.CanExecute(null));
    }

    [Fact] public async Task Save_StoresTheNewStartDay_AndGoesBackToInUse()
    {
        var vm = await Open();
        await vm.UseCommand.ExecuteAsync(null);
        vm.StartDay = DayOfWeek.Wednesday;
        await vm.UseCommand.ExecuteAsync(null);

        Assert.Equal("In use", vm.UseLabel);
        using var db = _db.CreateDbContext();
        Assert.Equal(DayOfWeek.Wednesday, db.Users.Single(u => u.Id == _alice).SplitStartDay);
    }

    [Fact] public async Task ReopeningTheActiveSplit_ReadsInUse_WithoutTheAnimation()
    {
        await (await Open()).UseCommand.ExecuteAsync(null);
        var vm = await Open();
        Assert.Equal("In use", vm.UseLabel);
        Assert.False(vm.JustSaved);
    }
}
