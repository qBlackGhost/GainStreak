using GymApp.App;
using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

/// <summary>Phase 18b: the nudge, the return popup and the sessions that stop by themselves.</summary>
public sealed class LiveWorkoutWatcherTests : IDisposable
{
    private sealed class Clock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }

    /// <summary>Records what the watcher asks of the phone, and whether reminders were replaced.</summary>
    private sealed class RecordingScheduler : INotificationScheduler
    {
        public List<string> Calls { get; } = [];
        public (DateTimeOffset At, string Title, string Body)? Nudge { get; private set; }
        public bool IsSupported => true;
        public Task<bool> RequestPermissionAsync() => Task.FromResult(true);
        public void Replace(IReadOnlyList<PlannedReminder> reminders) => Calls.Add("replace");
        public void ScheduleWorkoutNudge(DateTimeOffset at, string title, string body)
        {
            Calls.Add("nudge");
            Nudge = (at, title, body);
        }
        public void CancelWorkoutNudge()
        {
            Calls.Add("cancel");
            Nudge = null;
        }
    }

    private static readonly DateOnly Mon = new(2026, 10, 5);
    private readonly TestDb _db = new();
    private readonly string _logDir = Directory.CreateTempSubdirectory("gymapp-watcher-").FullName;
    private readonly Clock _clock = new(new DateTime(2026, 10, 5, 18, 0, 0));
    private readonly RecordingScheduler _scheduler = new();

    /// <summary>The last state shown, and every call (0.20.0).</summary>
    private sealed class RecordingActivity : ILiveActivity
    {
        public List<string> Calls { get; } = [];
        public LiveActivityState? Shown { get; private set; }
        public void Show(LiveActivityState state) { Calls.Add("show"); Shown = state; }
        public void End() { Calls.Add("end"); Shown = null; }
    }

    private readonly RecordingActivity _activity = new();
    private readonly LiveWorkoutService _live;
    private readonly int _alice;
    private readonly List<int> _ex;
    private int _heard;

    public LiveWorkoutWatcherTests()
    {
        _alice = _db.AddUser("alice");
        var day = _db.AddSplit(_alice, ("Legs", DayOfWeek.Monday))[0];
        _ex = _db.AddDayExercises(_alice, day, 4, workingSets: 1);
        var workouts = new WorkoutService(_db, new StreakService(_db), new PointsService(_db));
        _live = new LiveWorkoutService(_db, workouts, new SetService(_db, workouts), new StreakService(_db), _clock);
    }

    public void Dispose()
    {
        _db.Dispose();
        Directory.Delete(_logDir, recursive: true);
    }

    private LiveWorkoutWatcher Watcher(bool phone = true)
    {
        var session = new SessionService();
        using (var db = _db.CreateDbContext()) session.SignIn(db.Users.Single(u => u.Id == _alice));
        var w = new LiveWorkoutWatcher(_live, _scheduler, session, new FakePlatform { HasLiveWorkout = phone }, new ErrorLog(_logDir),
            _activity);
        w.Listen(() => _heard++);
        return w;
    }

    private async Task Start() => Assert.True((await _live.StartAsync(_alice)).Success);

    private Task Set(int exercise) => _live.SaveSetAsync(_alice, exercise, new SetInput(SetKind.Working, 8, 60));

    private static readonly string NextName = "is next.";

    [Fact] public async Task Background_WithASet_SchedulesTheNudge_For12MinutesLater()
    {
        var w = Watcher();
        await Start();
        await Set(_ex[0]);
        var before = DateTimeOffset.Now;
        await w.WentToBackgroundAsync();

        var nudge = Assert.NotNull(_scheduler.Nudge);
        Assert.Equal("Continue your workout?", nudge.Title);
        Assert.EndsWith(NextName, nudge.Body);
        Assert.InRange(nudge.At - before, TimeSpan.FromMinutes(11.9), TimeSpan.FromMinutes(12.1));
    }

    [Fact] public async Task Background_WithoutASet_OrWithoutAWorkout_DoesNothing()
    {
        var w = Watcher();
        await w.WentToBackgroundAsync();
        await Start();
        await w.WentToBackgroundAsync();
        Assert.Empty(_scheduler.Calls);
    }

    [Fact] public async Task Foreground_CancelsTheNudge()
    {
        var w = Watcher();
        await Start();
        await Set(_ex[0]);
        await w.WentToBackgroundAsync();
        await w.CameToForegroundAsync();
        Assert.Null(_scheduler.Nudge);
        Assert.Equal(["nudge", "cancel"], _scheduler.Calls);   // and never replaces the reminders
    }

    [Fact] public async Task ThePopup_ComesOnlyAfter12MinutesAway()
    {
        var w = Watcher();
        await Start();
        await Set(_ex[0]);
        await w.WentToBackgroundAsync();

        _clock.Now = _clock.Now.AddMinutes(11);
        await w.CameToForegroundAsync();
        Assert.False(w.ReturnPending);
        Assert.Equal(0, _heard);

        _clock.Now = _clock.Now.AddMinutes(1);
        await w.CameToForegroundAsync();
        Assert.True(w.ReturnPending);
        Assert.Equal(1, _heard);
    }

    [Fact] public async Task AfterAKill_TheColdStartStillAsks()
    {
        await Start();
        await Set(_ex[0]);
        _clock.Now = _clock.Now.AddMinutes(30);   // killed: no background event, the last set's time counts
        var w = Watcher();
        await w.CameToForegroundAsync();
        Assert.True(w.ReturnPending);
    }

    [Fact] public async Task Continue_RestartsTheClock()
    {
        var w = Watcher();
        await Start();
        await Set(_ex[0]);
        _clock.Now = _clock.Now.AddMinutes(20);
        await w.CameToForegroundAsync();
        await w.ContinueAsync();
        Assert.False(w.ReturnPending);
        Assert.Equal(TimeSpan.Zero, await _live.AwayForAsync(_alice));
    }

    [Fact] public async Task TheDesktop_IsLeftAlone()
    {
        var w = Watcher(phone: false);
        await Start();
        await Set(_ex[0]);
        await w.WentToBackgroundAsync();
        _clock.Now = _clock.Now.AddMinutes(30);
        await w.CameToForegroundAsync();
        Assert.Equal(["cancel"], _scheduler.Calls);
        Assert.False(w.ReturnPending);
    }

    [Fact] public async Task AfterFourHours_TheSessionStops_AndTheNoteSaysItCounted()
    {
        var w = Watcher();
        await Start();
        foreach (var e in _ex.Take(3)) await Set(e);   // 3 of 4: full points
        _clock.Now = _clock.Now.AddHours(4);
        await w.TickAsync();
        Assert.Equal(1, _heard);
        Assert.Null(await _live.GetOpenAsync(_alice));

        var note = Assert.IsType<LiveStopNote>(await _live.GetStopNoteAsync(_alice));
        // Not verified, so 2 even at 70%
        Assert.Equal((false, 3, 4, true, PointsCalculator.ReducedWorkoutPoints), (note.AtMidnight, note.Filled, note.Total, note.Counted, note.WorkoutPoints));

        await _live.MarkStopNoteSeenAsync(_alice, note.Id);
        Assert.Null(await _live.GetStopNoteAsync(_alice));
    }

    [Fact] public async Task AtMidnight_TheSessionStops_AndTheNoteSaysItDidntCount()
    {
        _clock.Now = new DateTime(2026, 10, 5, 23, 30, 0);
        var w = Watcher();
        await Start();
        await Set(_ex[0]);
        _clock.Now = new DateTime(2026, 10, 6, 7, 0, 0);
        await w.CameToForegroundAsync();
        Assert.False(w.ReturnPending);

        var note = Assert.IsType<LiveStopNote>(await _live.GetStopNoteAsync(_alice));
        Assert.True(note.AtMidnight);
        Assert.False(note.Counted);
        using var db = _db.CreateDbContext();
        Assert.Equal(new DateTime(2026, 10, 6, 0, 0, 0), db.LiveWorkouts.Single().EndedAt);
    }

    [Fact] public async Task EndingItYourself_LeavesNoNote()
    {
        await Start();
        await _live.EndAsync(_alice);
        Assert.Null(await _live.GetStopNoteAsync(_alice));
    }

    [Fact] public async Task NotificationPermission_IsAskedOnlyAtTheFirstStart()
    {
        Assert.True(await _live.FirstStartAsync(_alice));
        Assert.False(await _live.FirstStartAsync(_alice));
    }

    // ===== The Live Activity (0.20.0) =====

    [Fact] public async Task Activity_ShowsThePercentOfWorkingSets_AndTheNextExercise()
    {
        var w = Watcher();
        await Start();
        await Set(_ex[0]);
        await w.WentToBackgroundAsync();

        var shown = _activity.Shown;
        Assert.NotNull(shown);
        Assert.Equal((25, 1, 4), (shown.Percent, shown.DoneSets, shown.PlannedSets));   // 1 of 4 single-set exercises
        Assert.NotNull(shown.NextExercise);
        Assert.Equal(_clock.Now.AddHours(4), shown.EndsAt);
    }

    [Fact] public async Task Activity_Ends_WhenTheSessionStopsByItself()
    {
        var w = Watcher();
        await Start();
        await w.CameToForegroundAsync();
        Assert.NotNull(_activity.Shown);

        _clock.Now = _clock.Now.AddHours(4);
        await w.TickAsync();
        Assert.Null(_activity.Shown);
        Assert.Equal("end", _activity.Calls[^1]);
    }

    [Fact] public async Task Activity_Ends_WithNoOpenWorkout_AndNeverShowsOnTheDesktop()
    {
        await Start();
        await Watcher(phone: false).CameToForegroundAsync();
        Assert.DoesNotContain("show", _activity.Calls);

        var w = Watcher();
        w.ShowActivity(null);
        Assert.Equal("end", _activity.Calls[^1]);
    }

    [Fact] public void ActivityState_AllDone_HasNoNextExercise()
    {
        var info = new LiveWorkoutInfo(1, Mon, null, "Legs", _clock.Now, _clock.Now, null, [],
            [], new CheckInStatus(0, 0, 0, false), false);
        var state = LiveWorkoutWatcher.ActivityState(info);
        Assert.Null(state.NextExercise);
        Assert.Equal(0, state.Percent);
    }
}
