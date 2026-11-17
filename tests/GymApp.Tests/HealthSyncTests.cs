using GymApp.App;
using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;

namespace GymApp.Tests;

/// <summary>Phase 20: what HealthSync puts in Health, and what it leaves alone.</summary>
public sealed class HealthSyncTests : IDisposable
{
    private sealed class Clock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }

    /// <summary>Health as a list, with per-kind write access.</summary>
    private sealed class FakeStore : IHealthStore
    {
        public List<HealthItem> Items { get; } = [];
        public HashSet<HealthKind> Denied { get; } = [];
        public bool FailSaves { get; set; }
        public bool IsSupported { get; set; } = true;
        public HealthTexts Texts { get; set; } = HealthTexts.AppleHealth;
        public bool NeedsInstall { get; set; }
        public int InstallPagesOpened { get; private set; }
        public void OpenInstallPage() => InstallPagesOpened++;
        public Task<bool> RequestAuthorizationAsync() => Task.FromResult(true);
        public HealthAccess WriteAccess(HealthKind kind) => Denied.Contains(kind) ? HealthAccess.Denied : HealthAccess.Allowed;
        public Task<IReadOnlyList<HealthItem>> ReadOwnAsync(DateTime from, DateTime to) =>
            Task.FromResult<IReadOnlyList<HealthItem>>(Items.Where(i => i.SyncId.StartsWith(HealthPlan.Prefix) && i.Start >= from && i.Start < to).ToList());
        public Task SaveAsync(HealthItem item)
        {
            if (FailSaves) throw new InvalidOperationException("no");
            Items.RemoveAll(i => i.SyncId == item.SyncId);
            Items.Add(item);
            return Task.CompletedTask;
        }
        public Task DeleteAsync(HealthItem item)
        {
            Items.RemoveAll(i => i.SyncId == item.SyncId);
            return Task.CompletedTask;
        }
        public void OpenSettings() { }

        // Reads (20c): weigh-ins are handed out after the anchor (their index), like an anchored query
        public HealthProfile Profile { get; set; } = new(null, null, null);
        public List<HealthWeight> Weights { get; } = [];
        public List<double> Steps { get; set; } = [];
        public Task<HealthProfile> ReadProfileAsync() => Task.FromResult(Profile);
        public Task<HealthWeights> ReadWeightsAsync(DateTime from, byte[]? anchor)
        {
            var start = anchor is { Length: 1 } ? anchor[0] : 0;
            var added = Weights.Skip(start).Where(w => w.At >= from).ToList();
            return Task.FromResult(new HealthWeights(added, [(byte)Weights.Count]));
        }
        public Task<IReadOnlyList<double>> ReadDailyStepsAsync(DateOnly from, DateOnly to) => Task.FromResult<IReadOnlyList<double>>(Steps);
    }

    private sealed class Setting(bool on) : IHealthSetting
    {
        public bool On { get; set; } = on;
        public Task<bool> IsEnabledAsync(int userId) => Task.FromResult(On);
    }

    private static readonly DateOnly Today = new(2026, 11, 9);
    private readonly TestDb _db = new();
    private readonly string _logDir = Directory.CreateTempSubdirectory("gymapp-health-").FullName;
    private readonly Clock _clock = new(new DateTime(2026, 11, 9, 21, 0, 0));
    private readonly FakeStore _store = new();
    private readonly Setting _setting = new(true);
    private readonly SessionService _session = new();
    private readonly int _alice;
    private readonly int _push;

    public HealthSyncTests()
    {
        _alice = _db.AddUser("alice");
        _push = _db.AddSplit(_alice, ("Push", DayOfWeek.Monday))[0];
        SignIn(_alice);
    }

    public void Dispose()
    {
        _db.Dispose();
        Directory.Delete(_logDir, recursive: true);
    }

    private void SignIn(int userId)
    {
        using var db = _db.CreateDbContext();
        _session.SignIn(db.Users.Single(u => u.Id == userId));
    }

    private HealthSync Sync() => new(_store, new HealthExportService(_db), _session, _setting, new ErrorLog(_logDir), _clock);

    private void Workout(DateOnly date, int startHour, int minutes, bool logged = true, bool ended = true, int? dayId = -1)
    {
        using var db = _db.CreateDbContext();
        var start = date.ToDateTime(new TimeOnly(startHour, 0));
        db.LiveWorkouts.Add(new LiveWorkout { UserId = _alice, Date = date, SplitDayId = dayId == -1 ? _push : dayId, StartedAt = start,
            LastActiveAt = start, EndedAt = ended ? start.AddMinutes(minutes) : null });
        if (logged) db.WorkoutLogs.Add(new WorkoutLog { UserId = _alice, Date = date, SplitDayId = dayId == -1 ? _push : dayId });
        db.SaveChanges();
    }

    private void Weight(DateOnly date, double kg, int? userId = null)
    {
        using var db = _db.CreateDbContext();
        db.WeightLogs.Add(new WeightLog { UserId = userId ?? _alice, Date = date, WeightKg = kg });
        db.SaveChanges();
    }

    private void Cardio(DateOnly date, int startHour, int? savedMinutes, bool ended = true)
    {
        using var db = _db.CreateDbContext();
        var start = date.ToDateTime(new TimeOnly(startHour, 0));
        db.CardioSessions.Add(new CardioSession { UserId = _alice, Date = date, StartedAt = start, EndedAt = ended ? start.AddMinutes(40) : null,
            SavedMinutes = savedMinutes, CountedSeconds = (savedMinutes ?? 0) * 60 });
        db.SaveChanges();
    }

    [Fact] public async Task FinishedWorkout_IsWritten_WithItsTimesAndDayName()
    {
        Workout(Today, 18, 65);
        await Sync().SyncAsync();

        var w = Assert.Single(_store.Items);
        Assert.Equal((HealthKind.StrengthWorkout, "Push"), (w.Kind, w.Title));
        Assert.Equal(Today.ToDateTime(new TimeOnly(18, 0)), w.Start);
        Assert.Equal(TimeSpan.FromMinutes(65), w.End - w.Start);
        Assert.Equal(HealthPlan.WorkoutId(_alice, Today), w.SyncId);
    }

    [Fact] public async Task WorkoutThatDidntCount_OrIsStillOpen_IsNotWritten()
    {
        Workout(Today.AddDays(-1), 18, 30, logged: false);   // ended below half
        Workout(Today, 18, 0, ended: false);                // still running (already logged at half)
        await Sync().SyncAsync();
        Assert.Empty(_store.Items);
    }

    [Fact] public async Task RestDayWorkout_IsCalledExtraWorkout()
    {
        Workout(Today, 9, 40, dayId: null);
        await Sync().SyncAsync();
        Assert.Equal(LiveWorkoutService.PickedName, Assert.Single(_store.Items).Title);
    }

    [Fact] public async Task Cardio_LastsItsSavedMinutes_AndUnderAMinuteIsNotWritten()
    {
        Cardio(Today, 19, savedMinutes: 25);
        Cardio(Today, 20, savedMinutes: null);
        Cardio(Today, 21, savedMinutes: 10, ended: false);
        await Sync().SyncAsync();

        var c = Assert.Single(_store.Items);
        Assert.Equal(HealthKind.CardioWorkout, c.Kind);
        Assert.Equal(TimeSpan.FromMinutes(25), c.End - c.Start);
    }

    [Fact] public async Task Weight_IsWritten_AndAChangeReplacesIt()
    {
        Weight(Today, 80.4);
        var sync = Sync();
        await sync.SyncAsync();
        Assert.Equal(80.4, Assert.Single(_store.Items).Value);

        using (var db = _db.CreateDbContext())
        {
            db.WeightLogs.Single().WeightKg = 79.9;
            db.SaveChanges();
        }
        _clock.Now = _clock.Now.AddMinutes(1);
        await sync.SyncAsync();
        Assert.Equal(79.9, Assert.Single(_store.Items).Value);
    }

    [Fact] public async Task Undo_OrDeletedWeighIn_IsRemovedFromHealth()
    {
        Workout(Today, 18, 60);
        Weight(Today, 80);
        var sync = Sync();
        await sync.SyncAsync();
        Assert.Equal(2, _store.Items.Count);

        using (var db = _db.CreateDbContext())
        {
            db.WorkoutLogs.RemoveRange(db.WorkoutLogs);
            db.WeightLogs.RemoveRange(db.WeightLogs);
            db.SaveChanges();
        }
        await sync.SyncAsync();
        Assert.Empty(_store.Items);
    }

    [Fact] public async Task NothingChanged_WritesNothing()
    {
        Workout(Today, 18, 60);
        var sync = Sync();
        await sync.SyncAsync();
        Assert.True((await sync.SyncAsync())!.IsEmpty);
    }

    [Fact] public async Task OnlyTheLast30Days()
    {
        Weight(Today.AddDays(-29), 80);
        Weight(Today.AddDays(-30), 81);
        await Sync().SyncAsync();
        Assert.Equal(80, Assert.Single(_store.Items).Value);
    }

    [Fact] public async Task OtherAccounts_AndOtherApps_AreLeftAlone()
    {
        var bob = _db.AddUser("bob");
        var at = Today.ToDateTime(new TimeOnly(8, 0));
        _store.Items.Add(new HealthItem(HealthPlan.WeightId(bob, Today), HealthKind.Weight, at, at, 90, Version: 1));
        _store.Items.Add(new HealthItem("withings-1", HealthKind.Weight, at, at, 81, Version: 1));
        await Sync().SyncAsync();   // alice has nothing to write
        Assert.Equal(2, _store.Items.Count);
    }

    [Fact] public async Task KindsTheUserDidntAllow_AreSkipped()
    {
        Workout(Today, 18, 60);
        Weight(Today, 80);
        _store.Denied.Add(HealthKind.Weight);
        await Sync().SyncAsync();
        Assert.Equal(HealthKind.StrengthWorkout, Assert.Single(_store.Items).Kind);
    }

    [Fact] public async Task Off_SignedOut_OrNoHealth_DoesNothing()
    {
        Weight(Today, 80);
        _setting.On = false;
        Assert.Null(await Sync().SyncAsync());
        _setting.On = true;
        _store.IsSupported = false;
        Assert.Null(await Sync().SyncAsync());
        _store.IsSupported = true;
        _session.SignOut();
        Assert.Null(await Sync().SyncAsync());
        Assert.Empty(_store.Items);
    }

    [Fact] public async Task FailedWrites_AreLogged_WithoutTheValues_AndRetriedNextTime()
    {
        Weight(Today, 80.4);
        _store.FailSaves = true;
        var sync = Sync();
        await sync.SyncAsync();
        var logged = string.Concat(Directory.GetFiles(_logDir).Select(File.ReadAllText));
        Assert.Contains("Apple Health", logged);
        Assert.DoesNotContain("80.4", logged);

        _store.FailSaves = false;
        await sync.SyncAsync();
        Assert.Single(_store.Items);
    }

    [Fact] public async Task WeighInsImportedFromHealth_AreNeverWrittenBack()
    {
        using (var db = _db.CreateDbContext())
        {
            db.WeightLogs.Add(new WeightLog { UserId = _alice, Date = Today, WeightKg = 80, FromHealth = true });
            db.SaveChanges();
        }
        Assert.True((await Sync().SyncAsync())!.IsEmpty);
    }

    [Fact] public async Task TheProfileSwitch_TurnsSyncOnAndOff_AndStampsWhenItWasTurnedOn()
    {
        var profiles = new ProfileService(_db);
        var sync = new HealthSync(_store, new HealthExportService(_db), _session, new ProfileHealthSetting(profiles), new ErrorLog(_logDir), _clock);
        Weight(Today, 80);
        Assert.Null(await sync.SyncAsync());   // off for a new account

        Assert.True((await profiles.SetHealthEnabledAsync(_alice, true)).Success);
        await sync.SyncAsync();
        Assert.Single(_store.Items);
        using (var db = _db.CreateDbContext())
            Assert.NotNull(db.UserProfiles.Single(p => p.UserId == _alice).HealthEnabledAt);

        await profiles.SetHealthEnabledAsync(_alice, false);
        using (var db = _db.CreateDbContext())
        {
            db.WeightLogs.RemoveRange(db.WeightLogs);
            db.SaveChanges();
        }
        Assert.Null(await sync.SyncAsync());
        Assert.Single(_store.Items);   // off: what was written stays in Health
    }

    private async Task<(App.ViewModels.HealthViewModel Vm, ProfileService Profiles)> CardAsync()
    {
        var profiles = new ProfileService(_db);
        var sync = new HealthSync(_store, new HealthExportService(_db), _session, new ProfileHealthSetting(profiles), new ErrorLog(_logDir), _clock);
        var vm = new App.ViewModels.HealthViewModel(_store, profiles, sync, () => _session.CurrentUser?.Id);
        await vm.LoadAsync();
        return (vm, profiles);
    }

    private static async Task Until(Func<Task<bool>> done)
    {
        for (var i = 0; i < 100 && !await done(); i++) await Task.Delay(20);
    }

    [Fact] public async Task Card_TurningOn_SavesIt_AndNamesWhatWasntAllowed()
    {
        _store.Denied.Add(HealthKind.Weight);
        var (vm, profiles) = await CardAsync();
        Assert.False(vm.IsOn);

        vm.IsOn = true;
        await Until(() => profiles.IsHealthEnabledAsync(_alice));
        await Until(() => Task.FromResult(vm.Problem is not null));
        Assert.True(await profiles.IsHealthEnabledAsync(_alice));
        Assert.Equal("Weigh-ins are off for GainStreak in Health, so they aren't saved there.", vm.Problem);
        Assert.True(vm.CanOpenSettings);
    }

    [Fact] public async Task Card_TurningOff_SaysWhatStaysInHealth()
    {
        var (vm, profiles) = await CardAsync();
        await profiles.SetHealthEnabledAsync(_alice, true);
        await vm.LoadAsync();
        Assert.True(vm.IsOn);

        vm.IsOn = false;
        await Until(async () => !await profiles.IsHealthEnabledAsync(_alice));
        await Until(() => Task.FromResult(vm.Note is not null));
        Assert.Equal("What GainStreak already saved stays in Health. You can delete it in the Health app.", vm.Note);
    }

    // ===== Health Connect (Phase 23): the same card in Android's words =====

    [Fact] public async Task Card_OnTheIphone_KeepsAppleHealthsWords()
    {
        var (vm, _) = await CardAsync();
        Assert.Equal("Apple Health", vm.Texts.Title);
        Assert.Equal("Saves your workouts, cardio and weigh-ins to this iPhone's Health app, and reads your weight, height, birth date and steps from it.",
            vm.Texts.Description);
        Assert.Equal("Use Health's", vm.UseStoreText);
        Assert.Equal("Apple Health couldn't be opened. Try again.", vm.NoSheetText);
    }

    [Fact] public async Task Card_OnAndroid_SpeaksOfHealthConnect()
    {
        _store.Texts = HealthTexts.HealthConnect;
        _store.Denied.Add(HealthKind.StrengthWorkout);
        var (vm, profiles) = await CardAsync();
        Assert.Equal("Health Connect", vm.Texts.Title);
        Assert.Equal("Use Health Connect's", vm.UseStoreText);

        vm.IsOn = true;
        await Until(() => Task.FromResult(vm.Problem is not null));
        Assert.Equal("Workouts are off for GainStreak in Health Connect, so they aren't saved there.", vm.Problem);

        vm.IsOn = false;
        await Until(() => Task.FromResult(vm.Note is not null));
        Assert.Equal("What GainStreak already saved stays in Health Connect. You can delete it in Health Connect.", vm.Note);
        Assert.False(await profiles.IsHealthEnabledAsync(_alice));
    }

    [Fact] public async Task Card_WhenHealthConnectMustBeInstalled_OpensItsPage_AndStaysOff()
    {
        _store.Texts = HealthTexts.HealthConnect;
        _store.NeedsInstall = true;
        var (vm, profiles) = await CardAsync();

        vm.IsOn = true;
        await Until(() => Task.FromResult(vm.Problem is not null));
        Assert.False(vm.IsOn);
        Assert.False(await profiles.IsHealthEnabledAsync(_alice));
        Assert.Equal(1, _store.InstallPagesOpened);
        Assert.Equal("Health Connect isn't on this phone yet. Install it from Google Play, then turn this on again.", vm.Problem);
        Assert.False(vm.CanOpenSettings);
    }

    [Fact] public async Task Sync_DoesNothing_UntilHealthConnectIsInstalled()
    {
        var profiles = new ProfileService(_db);
        await profiles.SetHealthEnabledAsync(_alice, true);   // turned on, then Health Connect was uninstalled
        Weight(Today, 80);
        _store.NeedsInstall = true;
        var sync = new HealthSync(_store, new HealthExportService(_db), _session, new ProfileHealthSetting(profiles),
            new ErrorLog(_logDir), _clock, new HealthImportService(_db));
        Assert.Null(await sync.SyncAsync());
        Assert.Null(await sync.ReadAsync(force: true));
        Assert.Empty(_store.Items);
    }

    // ===== Reading from Health (20c) =====

    private async Task<(HealthSync Sync, ProfileService Profiles, HealthImportService Import)> ReaderAsync(DateTime? enabledAt = null)
    {
        var profiles = new ProfileService(_db);
        await profiles.SetHealthEnabledAsync(_alice, true);
        if (enabledAt is { } at)
            using (var db = _db.CreateDbContext())
            {
                db.UserProfiles.Single(p => p.UserId == _alice).HealthEnabledAt = at;
                db.SaveChanges();
            }
        var import = new HealthImportService(_db);
        return (new HealthSync(_store, new HealthExportService(_db), _session, new ProfileHealthSetting(profiles), new ErrorLog(_logDir), _clock, import),
            profiles, import);
    }

    private List<WeightLog> Weights()
    {
        using var db = _db.CreateDbContext();
        return db.WeightLogs.Where(w => w.UserId == _alice).OrderBy(w => w.Date).ToList();
    }

    [Fact] public async Task Read_ImportsWeighIns_MarkedFromHealth_AndOnlyNewOnesNextTime()
    {
        var (sync, _, _) = await ReaderAsync(_clock.Now);
        _store.Weights.Add(new HealthWeight(_clock.Now.AddDays(-3), 81.2, "withings-1"));
        _store.Weights.Add(new HealthWeight(_clock.Now.AddDays(-100), 85, "withings-0"));   // older than 90 days
        Assert.Equal(1, (await sync.ReadAsync(force: true))!.WeighInsImported);
        var w = Assert.Single(Weights());
        Assert.True(w.FromHealth);
        Assert.Equal(81.2, w.WeightKg);

        _store.Weights.Add(new HealthWeight(_clock.Now.AddDays(-1), 80.9, "withings-2"));
        Assert.Equal(1, (await sync.ReadAsync(force: true))!.WeighInsImported);   // only the new one
        Assert.Equal(2, Weights().Count);
    }

    [Fact] public async Task Read_NeverOverwritesATypedWeighIn_AndTypingOverAnImportedOneSendsItToHealth()
    {
        var (sync, profiles, _) = await ReaderAsync(_clock.Now);
        Weight(Today, 79);   // typed
        _store.Weights.Add(new HealthWeight(_clock.Now.AddHours(-1), 82, "withings-1"));
        _store.Weights.Add(new HealthWeight(_clock.Now.AddDays(-2), 81, "withings-2"));
        await sync.ReadAsync(force: true);
        Assert.Equal([(Today.AddDays(-2), 81.0, true), (Today, 79.0, false)], Weights().Select(w => (w.Date, w.WeightKg, w.FromHealth)));

        // Typing over the imported day makes it the user's: it goes to Health from then on
        Assert.True((await profiles.LogWeightAsync(_alice, 80.5, Today.AddDays(-2))).Success);
        Assert.False(Weights()[0].FromHealth);
        await sync.SyncAsync();
        Assert.Contains(_store.Items, i => i.SyncId == HealthPlan.WeightId(_alice, Today.AddDays(-2)) && i.Value == 80.5);
    }

    [Fact] public async Task Read_FillsEmptyFields_AndOffersDifferences_KeepMineIsRemembered()
    {
        var (sync, profiles, import) = await ReaderAsync();
        await profiles.SaveAsync(_alice, new ProfileInput(Sex.Male, null, 175, null, UnitSystem.Metric), Today);
        _store.Profile = new HealthProfile(182, new DateOnly(1994, 2, 3), Sex.Male);

        var r = (await sync.ReadAsync(force: true))!;
        Assert.True(r.ProfileFilled);   // the birth date was empty
        Assert.Equal(new HealthProfileOffer(182, null, null), r.Offer);
        using (var db = _db.CreateDbContext())
        {
            var p = db.UserProfiles.Single(x => x.UserId == _alice);
            Assert.Equal((new DateOnly(1994, 2, 3), 175.0), (p.BirthDate!.Value, p.HeightCm!.Value));   // typed height kept
        }

        await import.AnswerProfileOfferAsync(_alice, r.Offer!, useHealth: false);
        Assert.Null((await sync.ReadAsync(force: true))!.Offer);   // not asked again for the same values
        _store.Profile = _store.Profile with { HeightCm = 183 };
        Assert.NotNull((await sync.ReadAsync(force: true))!.Offer);   // a new value is

        await import.AnswerProfileOfferAsync(_alice, new HealthProfileOffer(183, null, null), useHealth: true);
        using (var db = _db.CreateDbContext())
            Assert.Equal(183, db.UserProfiles.Single(x => x.UserId == _alice).HeightCm);
    }

    [Fact] public async Task Read_SuggestsALevelFromSteps_OnceUntilItChanges()
    {
        var (sync, _, import) = await ReaderAsync();
        _store.Steps = Enumerable.Repeat(9800.0, 20).ToList();
        var r = (await sync.ReadAsync(force: true))!;
        Assert.Equal((ActivityLevel.Active, 9800.0), (r.StepsSuggestion!.Value, r.AverageSteps!.Value));

        await import.AnswerStepsSuggestionAsync(_alice, ActivityLevel.Active, use: false);
        Assert.Null((await sync.ReadAsync(force: true))!.StepsSuggestion);
        _store.Steps = Enumerable.Repeat(14000.0, 20).ToList();
        Assert.Equal(ActivityLevel.VeryActive, (await sync.ReadAsync(force: true))!.StepsSuggestion);

        await import.AnswerStepsSuggestionAsync(_alice, ActivityLevel.VeryActive, use: true);
        using var db = _db.CreateDbContext();
        Assert.Equal(ActivityLevel.VeryActive, db.UserProfiles.Single(x => x.UserId == _alice).ActivityLevel);
    }

    [Fact] public async Task Read_AtMostOnceAnHour_UnlessProfileAsks_AndNeverWhenOff()
    {
        var (sync, profiles, _) = await ReaderAsync();
        Assert.NotNull(await sync.ReadAsync(force: false));
        Assert.Null(await sync.ReadAsync(force: false));
        Assert.NotNull(await sync.ReadAsync(force: true));
        _clock.Now = _clock.Now.AddHours(1);
        Assert.NotNull(await sync.ReadAsync(force: false));

        await profiles.SetHealthEnabledAsync(_alice, false);
        Assert.Null(await sync.ReadAsync(force: true));
    }

    [Fact] public async Task Card_ShowsTheOfferAndTheSteps_AndAnswersThem()
    {
        var (sync, profiles, import) = await ReaderAsync();
        await profiles.SaveAsync(_alice, new ProfileInput(null, null, 175, null, UnitSystem.Metric), Today);
        _store.Profile = new HealthProfile(182, null, null);
        _store.Steps = Enumerable.Repeat(9800.0, 20).ToList();
        var changed = 0;
        var vm = new App.ViewModels.HealthViewModel(_store, profiles, sync, () => _session.CurrentUser?.Id, import,
            () => UnitSystem.Metric, () => changed++);
        await vm.LoadAsync();

        Assert.Equal("Health says: height 182 cm.", vm.OfferText);
        Assert.Equal("Your steps say Active: 9,800 a day over the last 30 days.", vm.StepsText);
        await vm.UseHealthProfileCommand.ExecuteAsync(null);
        await vm.NotNowStepsCommand.ExecuteAsync(null);
        Assert.Null(vm.OfferText);
        Assert.Null(vm.StepsText);
        Assert.Equal(1, changed);   // Profile reloads for the height it now has
    }

    [Fact] public void TheAppRegistersHealth_AndOtherHeadsHaveNone() =>
        Assert.False(((IPlatform)new FakePlatform()).CreateHealthStore().IsSupported);
}
