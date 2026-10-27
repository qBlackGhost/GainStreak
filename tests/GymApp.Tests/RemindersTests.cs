using GymApp.App;
using GymApp.App.ViewModels;
using GymApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GymApp.Tests;

/// <summary>Profile → Reminders and keeping the scheduled reminders current (16g), against a fake scheduler, in the
/// TestBench's local mode with a split chosen.</summary>
[Collection(DataDirectoryCollection.Name)]
public sealed class RemindersTests : IAsyncLifetime
{
    private sealed class FakeScheduler : INotificationScheduler
    {
        public bool Allow { get; set; } = true;
        public int PermissionAsked { get; private set; }
        public IReadOnlyList<PlannedReminder>? Scheduled { get; private set; }
        public bool IsSupported => true;
        public Task<bool> RequestPermissionAsync() { PermissionAsked++; return Task.FromResult(Allow); }
        public void Replace(IReadOnlyList<PlannedReminder> reminders) => Scheduled = reminders;
    }

    private readonly TempDataDirectory _data = new();
    private readonly FakeScheduler _scheduler = new();
    private ServiceProvider _sp = null!;
    private int _userId;

    public async Task InitializeAsync()
    {
        _sp = AppServices.Create(localSocial: true, new FakePlatform { Notifications = _scheduler }).BuildServiceProvider();
        Assert.Null(await AppStartup.PrepareAsync(_sp, localSocial: true));
        Assert.True((await _sp.GetRequiredService<IAccountService>().RegisterAsync("alice", "alice@example.com", "correct horse 1")).Success);
        _userId = _sp.GetRequiredService<SessionService>().CurrentUser!.Id;
        var splits = _sp.GetRequiredService<SplitService>();
        var preset = (await splits.GetPresetsAsync())[0];
        Assert.True(await splits.SelectAsync(_userId, preset.Id, preset.Variants.FirstOrDefault()?.Id, null));
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        _data.Dispose();
    }

    private RemindersViewModel Reminders() => new(_sp.GetRequiredService<ReminderService>(), _scheduler,
        _sp.GetRequiredService<ReminderSync>(), () => _userId);

    /// <summary>Lets the view model's fire-and-forget save finish.</summary>
    private static async Task Settle() { for (var i = 0; i < 20; i++) await Task.Delay(20); }

    [Fact] public async Task ANewUser_HasThemOff_WithBothKindsReady()
    {
        var s = await _sp.GetRequiredService<ReminderService>().GetSettingsAsync(_userId);
        Assert.Equal(new ReminderSettings(false, true, new TimeOnly(18, 0), true), s);
        Assert.Empty(await _sp.GetRequiredService<ReminderService>().PlanAsync(_userId, DateTime.Now));
    }

    [Fact] public async Task TurningOn_AsksPermission_ThenSavesAndSchedules()
    {
        var vm = Reminders();
        await vm.LoadAsync();

        vm.IsOn = true;
        await Settle();

        Assert.Equal(1, _scheduler.PermissionAsked);
        Assert.True((await _sp.GetRequiredService<ReminderService>().GetSettingsAsync(_userId)).Enabled);
        Assert.NotNull(_scheduler.Scheduled);
        Assert.NotEmpty(_scheduler.Scheduled!);   // a week of a split always has training days
        Assert.Null(vm.Note);
    }

    [Fact] public async Task ARefusal_TurnsTheSwitchBackOff_AndSaysWhere()
    {
        _scheduler.Allow = false;
        var vm = Reminders();
        await vm.LoadAsync();

        vm.IsOn = true;
        await Settle();

        Assert.False(vm.IsOn);
        Assert.Equal(RemindersViewModel.RefusedText, vm.Note);
        Assert.False((await _sp.GetRequiredService<ReminderService>().GetSettingsAsync(_userId)).Enabled);
    }

    [Fact] public async Task TheTimeAndEachKind_AreSaved()
    {
        var vm = Reminders();
        await vm.LoadAsync();
        vm.IsOn = true;
        await Settle();

        vm.TrainingTime = new TimeSpan(7, 30, 0);
        vm.MissedOn = false;
        await Settle();

        var s = await _sp.GetRequiredService<ReminderService>().GetSettingsAsync(_userId);
        Assert.Equal(new TimeOnly(7, 30), s.TrainingTime);
        Assert.False(s.MissedDay);
        Assert.All(_scheduler.Scheduled!, r => Assert.Equal(ReminderKind.TrainingDay, r.Kind));
    }

    [Fact] public async Task SigningOut_ClearsThem()
    {
        var vm = Reminders();
        await vm.LoadAsync();
        vm.IsOn = true;
        await Settle();
        Assert.NotEmpty(_scheduler.Scheduled!);

        await _sp.GetRequiredService<IAccountService>().LogOutAsync();
        await Settle();

        Assert.Empty(_scheduler.Scheduled!);
    }

    [Fact] public async Task TheDesktop_HasNone()
    {
        await using var sp = AppServices.Create(localSocial: true, new FakePlatform()).BuildServiceProvider();
        Assert.False(sp.GetRequiredService<INotificationScheduler>().IsSupported);
        Assert.False(new RemindersViewModel(sp.GetRequiredService<ReminderService>(), NoNotificationScheduler.Instance,
            sp.GetRequiredService<ReminderSync>(), () => null).IsSupported);
    }
}
