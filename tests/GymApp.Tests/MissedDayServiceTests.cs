using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using GymApp.Services;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

public class MissedDayServiceTests : IDisposable
{
    // 2026-10-05 is a Monday. Alice has the PPL preset from Monday: Mon Push, Tue Pull, Wed Legs, Thu Rest,
    // Fri Push, Sat Pull, Sun Legs. Mon is logged, Tue is missed, so on Wed she has a choice to make.
    private static readonly DateOnly Mon = new(2026, 10, 5);
    private static DateOnly D(int n) => Mon.AddDays(n);
    private const int Push = 1, Pull = 2, Legs = 3;   // seeded PPL split days

    private readonly TestDb _db = new();
    private readonly MissedDayService _svc;
    private readonly WorkoutService _workouts;
    private readonly int _alice;

    public MissedDayServiceTests()
    {
        var streaks = new StreakService(_db);
        var points = new PointsService(_db);
        _svc = new MissedDayService(_db, streaks, points);
        _workouts = new WorkoutService(_db, streaks, points);
        _alice = _db.AddUser("alice");
        Seed(db =>
        {
            var u = db.Users.Single(x => x.Id == _alice);
            u.SelectedSplitId = 1;
            u.SelectedVariantId = 1;
            u.SplitStartDay = DayOfWeek.Monday;
            db.UserSchedules.Add(new UserSchedule { UserId = _alice, EffectiveFrom = Mon, WeekPattern = "WWWRWWW", SplitId = 1 });
            db.WorkoutLogs.Add(new WorkoutLog { UserId = _alice, Date = D(0), SplitDayId = Push });
        });
        // one exercise of one working set on every PPL day, so a single set makes any day pass the check-in rule
        foreach (var day in new[] { Push, Pull, Legs })
            _db.AddDayExercises(_alice, day, 1, workingSets: 1);
    }

    public void Dispose() => _db.Dispose();

    private void Seed(Action<AppDbContext> change)
    {
        using var db = _db.CreateDbContext();
        change(db);
        db.SaveChanges();
    }

    private void Logs(params int[] days) =>
        Seed(db => db.WorkoutLogs.AddRange(days.Select(d => new WorkoutLog { UserId = _alice, Date = D(d) })));

    private void Chosen(int missed, MissChoice choice) =>
        Seed(db => db.MissedDayChoices.Add(new MissedDayChoice { UserId = _alice, Date = D(missed), Choice = choice }));

    /// <summary>Saves the one set the check-in rule needs today.</summary>
    private void SetToday(DateOnly date) => Seed(db =>
    {
        var exercise = db.UserDayExercises.First(x => x.UserId == _alice).ExerciseId;
        db.SetLogs.Add(new SetLog { UserId = _alice, ExerciseId = exercise, Date = date, SetNumber = 1, Kind = SetKind.Working, Reps = 8, WeightKg = 50 });
    });

    private async Task<LogResult> CheckIn(int day)
    {
        SetToday(D(day));
        return await _workouts.LogTodayAsync(_alice, today: D(day));
    }

    private List<MissedDayChoice> Choices()
    {
        using var db = _db.CreateDbContext();
        return db.MissedDayChoices.AsNoTracking().Where(c => c.UserId == _alice).OrderBy(c => c.Date).ToList();
    }

    private static MissChoice[] Offered(MissedDayPrompt p) => p.Options.Select(o => o.Choice).ToArray();

    // ===== Prompt =====

    [Fact] public async Task Prompt_OnTheDayAfterAMiss_OffersAllThreeOptions()
    {
        var p = (await _svc.GetPromptAsync(_alice, D(2)))!;
        Assert.Equal(D(1), p.MissedDate);
        Assert.Equal("Pull", p.MissedDayName);
        Assert.Equal(2, p.StreakAtStake);
        Assert.Equal(1, p.ShiftWeeksLeft);
        Assert.Equal(2, p.MakeUpsLeft);
        Assert.Equal([MissChoice.ShiftWeek, MissChoice.ShiftToRest, MissChoice.Continue], Offered(p));
        Assert.Empty(p.Notes);

        Assert.Equal("Pull today, and every day after moves one day later. Your rest day moves from Thursday to Friday.",
            p.Options[0].Preview);
        Assert.Equal(["Pull", "Legs", "Rest", "Push", "Pull", "Legs", "Push"], p.Options[0].NewWeek!.Select(d => d.Title));
        Assert.Equal("Pull today, Legs on Thursday instead of resting, back to normal on Friday.", p.Options[1].Preview);
        Assert.Equal("Today stays Legs. Your streak resets to 0.", p.Options[2].Preview);
        Assert.Equal("Reset your 2-day streak to 0?", p.Options[2].ConfirmText);
    }

    [Fact] public async Task Prompt_IsNull_WithoutAMissToChoose()
    {
        Assert.Null(await _svc.GetPromptAsync(_alice, D(1)));   // Tue: nothing missed yet
        Assert.Null(await _svc.GetPromptAsync(_alice, D(3)));   // Thu: the deadline passed, the streak broke
    }

    [Fact] public async Task Prompt_HidesShiftWeek_WithoutASplit()
    {
        Seed(db => db.Users.Single(u => u.Id == _alice).SelectedSplitId = null);
        var p = (await _svc.GetPromptAsync(_alice, D(2)))!;
        Assert.Equal([MissChoice.ShiftToRest, MissChoice.Continue], Offered(p));
        Assert.Equal("Workout", p.MissedDayName);
    }

    [Fact] public async Task Prompt_HidesMakeUp_WithoutARestDay()
    {
        Seed(db => db.UserSchedules.Single(s => s.UserId == _alice).WeekPattern = "WWWWWWW");
        Seed(db => db.Users.Single(u => u.Id == _alice).SelectedSplitId = null);
        Assert.Equal([MissChoice.Continue], Offered((await _svc.GetPromptAsync(_alice, D(2)))!));
    }

    // ===== Choosing =====

    [Fact] public async Task ShiftToRest_StoresTheRow_AndLeavesTheStreakAtRisk()
    {
        var r = await _svc.ChooseAsync(_alice, MissChoice.ShiftToRest, D(2));
        Assert.True(r.Success);
        Assert.Equal(new StreakResult(2, 2, StreakStatus.AtRisk, D(1)), r.Streak);
        Assert.Equal(5, r.Points!.NewTotal);   // Mon's workout
        var row = Assert.Single(Choices());
        Assert.Equal((D(1), MissChoice.ShiftToRest), (row.Date, row.Choice));
    }

    [Fact] public async Task Continue_ResetsTheStreak()
    {
        var r = await _svc.ChooseAsync(_alice, MissChoice.Continue, D(2));
        Assert.True(r.Success);
        Assert.Equal(new StreakResult(0, 1, StreakStatus.Broken), r.Streak);
        Assert.Equal(5, r.Points!.NewTotal);
        Assert.Equal(MissChoice.Continue, Assert.Single(Choices()).Choice);

        // today starts a new one
        var log = await CheckIn(2);
        Assert.Equal(new StreakResult(1, 1, StreakStatus.Active), log.Streak);
    }

    [Fact] public async Task ShiftWeek_OnAPreset_MovesTheStartDay_AndRecordsTheNewWeekFromToday()
    {
        var r = await _svc.ChooseAsync(_alice, MissChoice.ShiftWeek, D(2));
        Assert.True(r.Success);
        Assert.Equal(StreakStatus.AtRisk, r.Streak!.Status);
        Assert.Equal(5, r.Points!.NewTotal);
        Assert.Equal(MissChoice.ShiftWeek, Assert.Single(Choices()).Choice);

        using (var db = _db.CreateDbContext())
        {
            Assert.Equal(DayOfWeek.Tuesday, db.Users.Single(u => u.Id == _alice).SplitStartDay);
            var rows = db.UserSchedules.Where(s => s.UserId == _alice).OrderBy(s => s.EffectiveFrom).ToList();
            Assert.Equal([(Mon, "WWWRWWW"), (D(2), "WWWWRWW")], rows.Select(s => (s.EffectiveFrom, s.WeekPattern)));
        }

        // today is now Pull, and logging it keeps the streak with nothing more owed
        Assert.Equal("Pull", (await _workouts.GetTodayAsync(_alice, D(2))).Title);
        var log = await CheckIn(2);
        Assert.Equal(new StreakResult(3, 3, StreakStatus.Active), log.Streak);
        Assert.Equal("Legs", (await _workouts.GetTodayAsync(_alice, D(3))).Title);
        Assert.Equal("Rest day", (await _workouts.GetTodayAsync(_alice, D(4))).Title);
    }

    [Fact] public async Task ShiftWeek_OnSunday_WrapsToMonday()
    {
        Seed(db => db.Users.Single(u => u.Id == _alice).SplitStartDay = DayOfWeek.Sunday);
        Seed(db => db.UserSchedules.Single(s => s.UserId == _alice).WeekPattern = "WWRWWWW");   // PPL from Sunday
        Assert.True((await _svc.ChooseAsync(_alice, MissChoice.ShiftWeek, D(2))).Success);
        using var db = _db.CreateDbContext();
        Assert.Equal(DayOfWeek.Monday, db.Users.Single(u => u.Id == _alice).SplitStartDay);
    }

    [Fact] public async Task ShiftWeek_ReplacesAScheduleRowAlreadyThereForToday()
    {
        Seed(db => db.UserSchedules.Add(new UserSchedule { UserId = _alice, EffectiveFrom = D(2), WeekPattern = "WWWRWWW", SplitId = 1 }));
        Assert.True((await _svc.ChooseAsync(_alice, MissChoice.ShiftWeek, D(2))).Success);
        using var db = _db.CreateDbContext();
        Assert.Equal("WWWWRWW", db.UserSchedules.Single(s => s.UserId == _alice && s.EffectiveFrom == D(2)).WeekPattern);
    }

    [Fact] public async Task ShiftWeek_OnACustomSplit_MovesEveryWeekday()
    {
        var bob = _db.AddUser("bob");
        var days = _db.AddSplit(bob, ("A", DayOfWeek.Monday), ("B", DayOfWeek.Tuesday), ("C", DayOfWeek.Sunday));
        Seed(db =>
        {
            db.UserSchedules.Add(new UserSchedule { UserId = bob, EffectiveFrom = Mon, WeekPattern = "WWRRRRW" });
            db.WorkoutLogs.Add(new WorkoutLog { UserId = bob, Date = D(0) });
        });

        // Tue (B) missed; on Wed bob shifts the week
        Assert.True((await _svc.ChooseAsync(bob, MissChoice.ShiftWeek, D(2))).Success);

        using (var db = _db.CreateDbContext())
        {
            var split = db.SplitDays.Where(d => days.Contains(d.Id)).OrderBy(d => d.Order).ToList();
            Assert.Equal([("C", DayOfWeek.Monday), ("A", DayOfWeek.Tuesday), ("B", DayOfWeek.Wednesday)],
                split.Select(d => (d.Name, d.Weekday!.Value)));
            Assert.Equal("WWWRRRR", db.UserSchedules.Single(s => s.UserId == bob && s.EffectiveFrom == D(2)).WeekPattern);
        }
        Assert.Equal("B", (await _workouts.GetTodayAsync(bob, D(2))).Title);
    }

    // ===== Weekly limits =====

    /// <summary>Mon..Fri of the first week logged except <paramref name="missed"/>; returns the day after it.</summary>
    private int MissOn(int missed)
    {
        Logs(Enumerable.Range(1, missed - 1).Where(d => d != 3).ToArray());   // Mon is already logged, Thu is rest
        return missed + 1;
    }

    [Fact] public async Task SecondShiftWeek_InTheSameWeek_IsRefused_ButSkipStillWorks()
    {
        var today = MissOn(5);                 // Sat missed, today Sun
        Chosen(1, MissChoice.ShiftWeek);       // an earlier shift this week

        var p = (await _svc.GetPromptAsync(_alice, D(today)))!;
        Assert.DoesNotContain(MissChoice.ShiftWeek, Offered(p));
        Assert.Equal(["You've already shifted your schedule this week."], p.Notes);
        Assert.False((await _svc.ChooseAsync(_alice, MissChoice.ShiftWeek, D(today))).Success);
        Assert.True((await _svc.ChooseAsync(_alice, MissChoice.Continue, D(today))).Success);
    }

    [Fact] public async Task ThirdMakeUp_InTheSameWeek_IsRefused()
    {
        var today = MissOn(5);
        Chosen(1, MissChoice.ShiftToRest);
        Chosen(2, MissChoice.ShiftToRest);

        var p = (await _svc.GetPromptAsync(_alice, D(today)))!;
        Assert.DoesNotContain(MissChoice.ShiftToRest, Offered(p));
        Assert.Equal(["You've already made up 2 workouts this week."], p.Notes);
        var r = await _svc.ChooseAsync(_alice, MissChoice.ShiftToRest, D(today));
        Assert.False(r.Success);
        Assert.Equal("You've already made up 2 workouts this week.", r.Error);
        Assert.True((await _svc.ChooseAsync(_alice, MissChoice.Continue, D(today))).Success);
    }

    [Fact] public async Task TheTwoShiftOptions_HaveSeparateLimits()
    {
        var today = MissOn(5);
        Chosen(1, MissChoice.ShiftWeek);
        Assert.Equal(2, (await _svc.GetPromptAsync(_alice, D(today)))!.MakeUpsLeft);

        Seed(db => db.MissedDayChoices.RemoveRange(db.MissedDayChoices));
        Chosen(1, MissChoice.ShiftToRest);
        Chosen(2, MissChoice.ShiftToRest);
        var p = (await _svc.GetPromptAsync(_alice, D(today)))!;
        Assert.Equal(1, p.ShiftWeeksLeft);
        Assert.Contains(MissChoice.ShiftWeek, Offered(p));
    }

    // ===== Refusals and the check-in =====

    [Fact] public async Task Choose_IsRefused_WhenThereIsNothingToChoose()
    {
        Assert.False((await _svc.ChooseAsync(_alice, MissChoice.Continue, D(1))).Success);   // Tue: PendingToday
        Assert.False((await _svc.ChooseAsync(_alice, MissChoice.Continue, D(3))).Success);   // Thu: a day late, broken
        Assert.Empty(Choices());
    }

    [Fact] public async Task Choose_IsFinal()
    {
        Assert.True((await _svc.ChooseAsync(_alice, MissChoice.ShiftToRest, D(2))).Success);
        Assert.False((await _svc.ChooseAsync(_alice, MissChoice.Continue, D(2))).Success);
        Assert.Equal(MissChoice.ShiftToRest, Assert.Single(Choices()).Choice);
    }

    [Fact] public async Task CheckIn_IsRefusedUntilAChoiceIsMade()
    {
        var refused = await CheckIn(2);
        Assert.False(refused.Success);
        Assert.Equal("Choose what to do about yesterday's missed workout first.", refused.Error);

        await _svc.ChooseAsync(_alice, MissChoice.ShiftToRest, D(2));
        Assert.True((await _workouts.LogTodayAsync(_alice, today: D(2))).Success);
    }

    [Fact] public async Task MakeUpChain_ChecksInYesterdaysDay_IncludingOnTheRestDayThatEndsIt()
    {
        await _svc.ChooseAsync(_alice, MissChoice.ShiftToRest, D(2));

        // Wed: Pull (Tue's workout) instead of Legs
        Assert.Equal(Pull, (await _workouts.GetCheckInAsync(_alice, today: D(2))).PlannedDay!.SplitDayId);
        Assert.Equal("Pull", (await _workouts.GetTodayAsync(_alice, D(2))).Title);
        var wed = await CheckIn(2);
        Assert.Equal(StreakStatus.Shifted, wed.Streak!.Status);
        Assert.Equal(Pull, (await _workouts.GetCheckInAsync(_alice, today: D(2))).PlannedDay!.SplitDayId);   // still, once logged

        // Thu (rest day): Legs, with no rest-day picker
        var thu = await _workouts.GetCheckInAsync(_alice, today: D(3));
        Assert.Equal(Legs, thu.PlannedDay!.SplitDayId);
        Assert.Empty(thu.PickableDays);
        // Rest days stopped adding in 0.18.1 (the rule starts on the first check-in here): the unverified make-up on
        // the rest day keeps the streak at 3 (Mon, Tue and Wed, both made up), and isn't shown on ice
        Assert.Equal(new StreakResult(3, 3, StreakStatus.Active), (await CheckIn(3)).Streak);

        // Fri: back to normal
        Assert.Equal(Push, (await _workouts.GetCheckInAsync(_alice, today: D(4))).PlannedDay!.SplitDayId);
    }

    [Fact] public async Task OldMissWithoutAChoice_AlsoChecksInYesterdaysDay()
    {
        Logs(2);   // Wed logged before choices existed
        Assert.Equal(Legs, (await _workouts.GetCheckInAsync(_alice, today: D(3))).PlannedDay!.SplitDayId);
    }

    [Fact] public async Task UndoAfterAChoice_KeepsTheChoice_AndGoesBackToAtRisk()
    {
        await _svc.ChooseAsync(_alice, MissChoice.ShiftToRest, D(2));
        await CheckIn(2);

        var undone = await _workouts.UndoTodayAsync(_alice, D(2));
        Assert.Equal(StreakStatus.AtRisk, undone.Streak!.Status);
        Assert.Single(Choices());
        Assert.Null(await _svc.GetPromptAsync(_alice, D(2)));
        Assert.True((await _workouts.LogTodayAsync(_alice, today: D(2))).Success);
    }

    // ===== Dashboard: today's workout =====

    [Fact] public async Task TodaysWorkout_DuringAMakeUpChain_IsYesterdaysDay()
    {
        await _svc.ChooseAsync(_alice, MissChoice.ShiftToRest, D(2));
        var wed = await _workouts.GetTodayWorkoutAsync(_alice, D(2));
        Assert.Equal("Pull", wed.DayName);
        Assert.Single(wed.Exercises);

        await CheckIn(2);
        Assert.Equal("Legs", (await _workouts.GetTodayWorkoutAsync(_alice, D(3))).DayName);   // the rest day ending the chain
        Assert.Equal("Push", (await _workouts.GetTodayWorkoutAsync(_alice, D(4))).DayName);   // back to normal
    }

    [Fact] public async Task TodaysWorkout_ShowsPlannedSets_AndLastTimesTopSet()
    {
        SetToday(D(0));   // Mon: one working set of 50 kg x 8 on the exercise every day shares
        Seed(db =>
        {
            var exercise = db.UserDayExercises.First(x => x.UserId == _alice).ExerciseId;
            db.SetLogs.Add(new SetLog { UserId = _alice, ExerciseId = exercise, Date = D(0), SetNumber = 2, Kind = SetKind.Working, Reps = 10, WeightKg = 50 });
            db.SetLogs.Add(new SetLog { UserId = _alice, ExerciseId = exercise, Date = D(0), SetNumber = 3, Kind = SetKind.Warmup, Reps = 10, WeightKg = 90 });
        });
        var tue = await _workouts.GetTodayWorkoutAsync(_alice, D(1));
        var e = Assert.Single(tue.Exercises);
        Assert.Equal((0, 1), (e.WarmupSets, e.WorkingSets));
        Assert.Equal((50.0, 10), (e.LastTopSet!.WeightKg, e.LastTopSet.Reps));   // heaviest working set, most reps; warm-ups ignored
    }

    [Fact] public async Task TodaysWorkout_OnARestDay_HasNoDay()
    {
        Logs(1, 2);
        var thu = await _workouts.GetTodayWorkoutAsync(_alice, D(3));
        Assert.True(thu.HasSplit);
        Assert.Null(thu.DayName);
    }
}
