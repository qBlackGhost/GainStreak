using GymApp.Contracts;
using GymApp.Data.Models;
using GymApp.App.ViewModels;
using GymApp.Services;
using GymApp.Social;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

public class ShareRulesTests
{
    private static readonly SharedExercise Squat = new("Back squat", "Quads", false, 1, 3);

    [Fact] public void Validate_AcceptsAWorkout_AndNamesTheFirstProblem()
    {
        var workout = new SharedWorkout("Legs", "PPL", [Squat]);
        Assert.Null(ShareRules.Validate(new ShareRequest([2], null, workout)));

        Assert.Equal(ShareRules.NoRecipientsMessage, ShareRules.Validate(new ShareRequest([], null, workout)));
        Assert.Equal(ShareRules.TooManyRecipientsMessage, ShareRules.Validate(new ShareRequest(Enumerable.Range(1, 11).ToList(), null, workout)));
        Assert.Equal(ShareRules.OneThingMessage, ShareRules.Validate(new ShareRequest([2], null, null)));
        Assert.Equal(ShareRules.EmptyMessage, ShareRules.Validate(new ShareRequest([2], null, workout with { Exercises = [] })));
        Assert.Equal(ShareRules.InvalidMessage,
            ShareRules.Validate(new ShareRequest([2], null, workout with { Exercises = [Squat with { WorkingSets = 99 }] })));
    }

    [Fact] public void Validate_CustomSplitDays_NeedOneWeekdayEach()
    {
        SharedDay Day(DayOfWeek d) => new("Legs", d, [Squat]);
        var ok = new SharedSplit("Mine", null, null, [Day(DayOfWeek.Monday), Day(DayOfWeek.Thursday)]);
        Assert.Null(ShareRules.Validate(new ShareRequest([2], ok, null)));
        Assert.Equal(ShareRules.InvalidMessage,
            ShareRules.Validate(new ShareRequest([2], ok with { Days = [Day(DayOfWeek.Monday), Day(DayOfWeek.Monday)] }, null)));
        Assert.Equal(ShareRules.InvalidMessage,
            ShareRules.Validate(new ShareRequest([2], ok with { PresetId = 1 }, null)));   // a preset needs its pattern
    }

    [Fact] public void Week_ShiftsACustomSplit_AndLaysOutAPreset()
    {
        SharedDay Day(string name, DayOfWeek? d) => new(name, d, [Squat]);
        var custom = new SharedSplit("Mine", null, null,
            [Day("A", DayOfWeek.Monday), Day("B", DayOfWeek.Wednesday), Day("C", DayOfWeek.Friday)]);
        Assert.Equal([(DayOfWeek.Tuesday, "A"), (DayOfWeek.Thursday, "B"), (DayOfWeek.Saturday, "C")],
            SplitShareService.Week(custom, DayOfWeek.Tuesday).Select(w => (w.Day.Weekday, w.Day.Name)));
        Assert.Equal([(DayOfWeek.Monday, "B"), (DayOfWeek.Wednesday, "C"), (DayOfWeek.Saturday, "A")],   // wraps the week
            SplitShareService.Week(custom, DayOfWeek.Saturday).Select(w => (w.Day.Weekday, w.Day.Name)));

        var ppl = new SharedSplit("Push / Pull / Legs", 1, "WWWRWWW", [Day("Push", null), Day("Pull", null), Day("Legs", null)]);
        Assert.Equal("Mon Push · Tue Pull · Wed Legs · Fri Push · Sat Pull · Sun Legs",
            string.Join(" · ", SplitShareService.Week(ppl, DayOfWeek.Monday).Select(w => $"{w.Day.Weekday.ToString()[..3]} {w.Day.Name}")));
        Assert.Equal(0, SplitShareService.DaysLeftOut(ppl, DayOfWeek.Monday));
        var fiveDays = ppl with { Pattern = "WRWRRRR", Days = [Day("A", null), Day("B", null), Day("C", null)] };
        Assert.Equal(1, SplitShareService.DaysLeftOut(fiveDays, DayOfWeek.Monday));   // 2 slots for 3 days
    }
}

public class SplitShareServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly SplitService _splits;
    private readonly SplitShareService _svc;
    private readonly int _alice, _bob;

    public SplitShareServiceTests()
    {
        _alice = _db.AddUser("alice");
        _bob = _db.AddUser("bob");
        _splits = new SplitService(_db, new PointsService(_db));
        _svc = new SplitShareService(_db, _splits);
    }

    public void Dispose() => _db.Dispose();

    private List<(string Name, int Warmup, int Working)> List(int userId, int dayId)
    {
        using var db = _db.CreateDbContext();
        return db.UserDayExercises.Where(x => x.UserId == userId && x.SplitDayId == dayId).OrderBy(x => x.Order)
            .Select(x => new { x.Exercise.Name, x.WarmupSets, x.WorkingSets }).AsEnumerable()
            .Select(x => (x.Name, x.WarmupSets, x.WorkingSets)).ToList();
    }

    private int AddCustomExercise(int userId, int dayId, string name)
    {
        using var db = _db.CreateDbContext();
        var e = new Exercise { Name = name, MuscleGroup = MuscleGroup.Glutes, OwnerUserId = userId };
        db.Exercises.Add(e);
        db.SaveChanges();
        var order = db.UserDayExercises.Count(x => x.UserId == userId && x.SplitDayId == dayId);
        db.UserDayExercises.Add(new UserDayExercise { UserId = userId, SplitDayId = dayId, ExerciseId = e.Id, Order = order + 1, WorkingSets = 4 });
        db.SaveChanges();
        return e.Id;
    }

    [Fact] public async Task Workout_LoadsIntoADayOfTheActiveSplit_CreatingCustomExercises()
    {
        var aliceLegs = _db.AddSplit(_alice, ("Legs", DayOfWeek.Monday))[0];
        _db.AddDayExercises(_alice, aliceLegs, 3, warmupSets: 1, workingSets: 4);
        AddCustomExercise(_alice, aliceLegs, "Sled push");
        var bobDays = _db.AddSplit(_bob, ("Upper", DayOfWeek.Tuesday), ("Lower", DayOfWeek.Friday));
        _db.AddDayExercises(_bob, bobDays[1], 1);   // the first built-in: alice's list has it too

        var workout = await _svc.BuildWorkoutAsync(_alice, aliceLegs);
        Assert.Equal(("Legs", "Test split", 4), (workout!.DayName, workout.SplitName, workout.Exercises.Count));
        Assert.True(workout.Exercises[3].IsCustom);

        var added = await _svc.ImportWorkoutAsync(_bob, workout, bobDays[1], CopyMode.Append);

        Assert.True(added.Success, added.Error);
        var lower = List(_bob, bobDays[1]);
        Assert.Equal(4, lower.Count);   // the shared first exercise was already there
        Assert.Equal(("Sled push", 0, 4), lower[3]);
        using (var db = _db.CreateDbContext())
            Assert.Equal("Sled push", db.Exercises.Single(e => e.OwnerUserId == _bob).Name);

        Assert.True((await _svc.ImportWorkoutAsync(_bob, workout, bobDays[1], CopyMode.Replace)).Success);
        Assert.Equal(workout.Exercises.Select(e => e.Name), List(_bob, bobDays[1]).Select(x => x.Name));
        using (var db = _db.CreateDbContext())
            Assert.Single(db.Exercises.Where(e => e.OwnerUserId == _bob));   // matched by name the second time
    }

    [Fact] public async Task Workout_OnlyGoesIntoTheActiveSplit()
    {
        var aliceDay = _db.AddSplit(_alice, ("Legs", DayOfWeek.Monday))[0];
        _db.AddDayExercises(_alice, aliceDay, 2);
        _db.AddSplit(_bob, ("Legs", DayOfWeek.Monday));
        var workout = (await _svc.BuildWorkoutAsync(_alice, aliceDay))!;

        var r = await _svc.ImportWorkoutAsync(_bob, workout, 3, CopyMode.Append);   // a preset day bob isn't using

        Assert.Equal((false, "Pick a day of your current split."), (r.Success, r.Error));
    }

    [Fact] public async Task CustomSplit_BecomesANewActiveSplit_ShiftedToTheStartDay()
    {
        var days = _db.AddSplit(_alice, ("Push", DayOfWeek.Monday), ("Pull", DayOfWeek.Wednesday), ("Legs", DayOfWeek.Friday));
        foreach (var d in days) _db.AddDayExercises(_alice, d, 2);
        var split = (await _svc.BuildSplitAsync(_alice, (await _svc.GetShareableSplitsAsync(_alice)).Single().SplitId))!;
        Assert.Equal((null, null, 3), (split.PresetId, split.Pattern, split.Days.Count));

        var r = await _svc.ImportSplitAsync(_bob, split, DayOfWeek.Tuesday, SplitImportMode.NewCustom);

        Assert.True(r.Success, r.Error);
        using (var db = _db.CreateDbContext())
        {
            var bobs = db.WorkoutSplits.Include(s => s.Days).Single(s => s.OwnerUserId == _bob);
            Assert.Equal("Test split", bobs.Name);
            Assert.Equal([(DayOfWeek.Tuesday, "Push"), (DayOfWeek.Thursday, "Pull"), (DayOfWeek.Saturday, "Legs")],
                bobs.Days.OrderBy(d => d.Order).Select(d => (d.Weekday!.Value, d.Name)));
            Assert.Equal(bobs.Id, db.Users.Single(u => u.Id == _bob).SelectedSplitId);
            Assert.All(bobs.Days, d => Assert.Equal(2, db.UserDayExercises.Count(x => x.UserId == _bob && x.SplitDayId == d.Id)));
        }

        Assert.True((await _svc.ImportSplitAsync(_bob, split, DayOfWeek.Monday, SplitImportMode.NewCustom)).Success);
        using (var db = _db.CreateDbContext())
            Assert.Contains("Test split (2)", db.WorkoutSplits.Where(s => s.OwnerUserId == _bob).Select(s => s.Name));
    }

    [Fact] public async Task Preset_ReplacesTheReceiversOwnExercises_WithTheSendersLayout()
    {
        Assert.True(await _splits.SelectAsync(_alice, 2, 2, DayOfWeek.Monday));   // Upper / Lower, 4 days a week
        _db.AddDayExercises(_alice, 4, 3);
        _db.AddDayExercises(_alice, 5, 2);
        _db.AddDayExercises(_bob, 4, 5);
        var split = (await _svc.BuildSplitAsync(_alice, 2))!;
        Assert.Equal((2, "WWRWWRR"), (split.PresetId, split.Pattern));

        var r = await _svc.ImportSplitAsync(_bob, split, DayOfWeek.Wednesday, SplitImportMode.ReplaceMine);

        Assert.True(r.Success, r.Error);
        Assert.Equal(List(_alice, 4), List(_bob, 4));
        Assert.Equal(List(_alice, 5), List(_bob, 5));
        using var db = _db.CreateDbContext();
        var bob = db.Users.Single(u => u.Id == _bob);
        Assert.Equal((2, 2, DayOfWeek.Wednesday), (bob.SelectedSplitId, bob.SelectedVariantId, bob.SplitStartDay));
        Assert.Empty(db.WorkoutSplits.Where(s => s.OwnerUserId == _bob));
    }

    [Fact] public async Task NewCustom_NeedsAFreeSlot()
    {
        var day = _db.AddSplit(_alice, ("Legs", DayOfWeek.Monday))[0];
        _db.AddDayExercises(_alice, day, 1);
        var split = (await _svc.BuildSplitAsync(_alice, (await _svc.GetShareableSplitsAsync(_alice)).Single().SplitId))!;
        for (var i = 0; i < SplitService.MaxCustomSplits; i++)
            await _splits.CreateAsync(_bob, $"Mine {i}", [new DayInput(DayOfWeek.Monday, "Day")]);

        var r = await _svc.ImportSplitAsync(_bob, split, DayOfWeek.Monday, SplitImportMode.NewCustom);

        Assert.Equal((false, SplitShareService.NoRoomMessage), (r.Success, r.Error));
    }
}

public class InboxShareTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gymapp-inbox-share-" + Guid.NewGuid().ToString("N"));
    private readonly SessionService _session = new();
    private readonly FakeShares _shares = new();
    private readonly InboxViewModel _vm;
    private readonly int _bob;
    private readonly List<int> _bobDays;

    public InboxShareTests()
    {
        _bob = _db.AddUser("bob");
        using (var db = _db.CreateDbContext()) _session.SignIn(db.Users.Single(u => u.Id == _bob));
        _bobDays = _db.AddSplit(_bob, ("Upper", DayOfWeek.Monday), ("Legs", DayOfWeek.Thursday));
        var friends = new FriendService(new LocalSocialStore(_db));
        var inbox = new InboxService(() => [new FriendRequestInboxSource(friends), new ShareInboxSource(_shares)],
            new InboxStore(_dir), _session);
        var splitShares = new SplitShareService(_db, new SplitService(_db, new PointsService(_db)));
        _vm = new InboxViewModel(inbox, friends, _session, () => null!, _shares, splitShares, new WorkoutMenuService(_db));
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private sealed class FakeShares : IShareService
    {
        public List<ReceivedShare> Waiting { get; } = [];

        public Task<ShareSendResult> SendAsync(ShareRequest request) => Task.FromResult(new ShareSendResult(true, Sent: 1));
        public Task<IReadOnlyList<ReceivedShare>?> GetIncomingAsync() => Task.FromResult<IReadOnlyList<ReceivedShare>?>(Waiting.ToList());

        public Task<string?> RemoveAsync(long id)
        {
            Waiting.RemoveAll(s => s.Id == id);
            return Task.FromResult<string?>(null);
        }
    }

    private static readonly SharedWorkout Legs = new("Legs", "PPL", [new("Back squat", "Quads", false, 1, 4), new("Hip thrust", "Glutes", true, 0, 3)]);

    [Fact] public async Task SharedWorkout_ShowsUnderWorkouts_AndLoadsIntoTheMatchingDay()
    {
        _shares.Waiting.Add(new ReceivedShare(7, 99, "alice", DateTime.UtcNow.AddHours(-3), null, Legs));
        await _vm.LoadAsync();

        var row = Assert.IsType<SharedWorkoutRowViewModel>(Assert.Single(_vm.Rows));
        Assert.Equal(("alice shared a workout", "Legs", "From PPL · 2 exercises · 8 sets"), (row.Headline, row.Title, row.Detail));
        Assert.Equal("Workouts 1", _vm.WorkoutsLabel);
        _vm.ShowRequestsCommand.Execute(null);
        Assert.Empty(_vm.Rows);
        _vm.ShowWorkoutsCommand.Execute(null);

        await ((SharedWorkoutRowViewModel)_vm.Rows.Single()).AddCommand.ExecuteAsync(null);
        Assert.True(_vm.WorkoutAdd.IsOpen);
        Assert.Equal("Legs", _vm.WorkoutAdd.Chosen!.Name);   // same name, picked for you
        await _vm.WorkoutAdd.ConfirmCommand.ExecuteAsync(null);

        Assert.False(_vm.WorkoutAdd.IsOpen);
        Assert.Empty(_shares.Waiting);
        Assert.Empty(_vm.Rows);
        Assert.Equal("You loaded Legs from alice into Legs", _vm.History.Single().Text);
        using var db = _db.CreateDbContext();
        // "Hip Thrust" is a built-in here, so the name matches it rather than making a custom one
        Assert.Equal(["Back squat", "Hip Thrust"], db.UserDayExercises.Where(x => x.UserId == _bob && x.SplitDayId == _bobDays[1])
            .OrderBy(x => x.Order).Select(x => x.Exercise.Name));
    }

    [Fact] public async Task SharedSplit_PopupPreviewsTheWeek_AndDismissLeavesNoLine()
    {
        var split = new SharedSplit("Push / Pull / Legs", 1, "WWWRWWW",
            [new("Push", null, Legs.Exercises), new("Pull", null, Legs.Exercises), new("Legs", null, Legs.Exercises)]);
        _shares.Waiting.Add(new ReceivedShare(8, 99, "alice", DateTime.UtcNow, split, null));
        await _vm.LoadAsync();

        var row = Assert.IsType<SharedSplitRowViewModel>(Assert.Single(_vm.Rows));
        Assert.Equal("Whole split · 3 days · 6 exercises", row.Detail);
        row.AddCommand.Execute(null);
        Assert.True(_vm.SplitAdd.CanReplaceMine);
        _vm.SplitAdd.Start = DayOfWeek.Monday;
        Assert.Equal("Mon Push · Tue Pull · Wed Legs · Fri Push · Sat Pull · Sun Legs", _vm.SplitAdd.WeekText);
        _vm.SplitAdd.CloseCommand.Execute(null);

        await row.DismissCommand.ExecuteAsync(null);

        Assert.Empty(_shares.Waiting);
        Assert.Empty(_vm.Rows);
        Assert.False(_vm.HasHistory);
    }
}
