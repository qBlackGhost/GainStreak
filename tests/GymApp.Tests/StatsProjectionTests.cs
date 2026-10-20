using GymApp.Core;

namespace GymApp.Tests;

public class StatsProjectionTests
{
    // 2026-10-05 is a Monday. D(0) = Mon ... D(7) = next Mon
    private static readonly DateOnly Mon = new(2026, 10, 5);
    private static DateOnly D(int offset) => Mon.AddDays(offset);

    // Upper/Lower: Mon W, Tue W, Wed R, Thu W, Fri W, Sat R, Sun R
    private static readonly ScheduleChange[] UpperLower = { new ScheduleChange(Mon, "WWRWWRR") };
    private static readonly Dictionary<DateOnly, MissChoice> NoChoices = new();

    private static IReadOnlyList<ProjectedStats> Run(int[] logged, int today, int days,
        IReadOnlyList<ScheduleChange>? schedule = null, IReadOnlyDictionary<DateOnly, MissChoice>? choices = null,
        IReadOnlyList<PointsAward>? ledger = null) =>
        StatsProjection.Project(logged.Select(D).ToList(), schedule ?? UpperLower, choices ?? NoChoices, [],
            ledger ?? [], D(today), days);

    [Fact] public void OneEntryPerDay_FromToday()
    {
        var stats = Run([0], 0, 14);
        Assert.Equal(Enumerable.Range(0, 14).Select(D), stats.Select(s => s.Date));
    }

    [Fact] public void FewerThanOneDay_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Run([0], 0, 0));

    [Fact] public void RestDayGrows_MissedDayBreaks() =>
        // Tue 2, Wed rest 3, Thu pending 3, Fri: Thu owed 4, Sat: Fri passed with the debt open -> 0
        Assert.Equal([2, 3, 3, 4, 0, 0], Run([0, 1], 1, 6).Select(s => s.CurrentStreak));

    [Fact] public void ContinueChoice_BreaksAtOnce()
    {
        Assert.Equal(2, Run([0], 2, 1)[0].CurrentStreak);   // no choice yet: Tue is owed
        Assert.Equal(0, Run([0], 2, 1, choices: new Dictionary<DateOnly, MissChoice> { [D(1)] = MissChoice.Continue })[0].CurrentStreak);
    }

    [Fact] public void NoSchedule_EveryDayIsTraining() =>
        // Tue pending 1, Wed: Tue owed 2, Thu: Wed passed with the debt open -> 0
        Assert.Equal([1, 1, 2, 0], Run([0], 0, 4, schedule: []).Select(s => s.CurrentStreak));

    [Fact] public void FullWeek_TotalStays_WeekPointsResetOnMonday()
    {
        // 4 workouts (20) + week complete (50). Projected past the window moving on, so the entries must stay frozen
        var stats = Run([0, 1, 3, 4], 4, 12);
        Assert.All(stats, s => Assert.Equal(70, s.TotalPoints));
        Assert.Equal([70, 70, 70, 0, 0, 0, 0, 0, 0, 0, 0, 0], stats.Select(s => s.WeekPoints));
    }

    [Fact] public void GraceWeekBonus_IsPaidOnceTheWeekEnds()
    {
        // Tue missed, made up on Wed (rest): Graced. +25 dated Sunday, paid on Monday
        var stats = Run([0, 2, 3, 4], 4, 4);
        Assert.Equal([20, 20, 20, 45], stats.Select(s => s.TotalPoints));
        Assert.Equal([20, 20, 20, 0], stats.Select(s => s.WeekPoints));   // dated Sunday, so it belongs to the week before
    }

    [Fact] public void ManualAdjustments_AreKept()
    {
        var stats = Run([0], 0, 2, ledger: [
            new PointsAward(PointsReason.Workout, 5, D(0), "workout:2026-10-05"),
            new PointsAward(PointsReason.Adjustment, -3, D(0), "adjust:test")]);
        Assert.All(stats, s => Assert.Equal(2, s.TotalPoints));
    }

    [Fact] public void EntriesBeforeTheWindow_AreFrozen()
    {
        // No log behind it any more, but it's older than the sync window, so it stays
        var stats = Run([0], 0, 1, ledger: [
            new PointsAward(PointsReason.Workout, 5, D(-30), "workout:2026-09-05")]);
        Assert.Equal(10, stats[0].TotalPoints);
    }

    [Fact] public void EntriesInTheWindow_FollowTheRules()
    {
        // A stale in-window entry the rules no longer want (its log was undone) is dropped
        var stats = Run([0], 1, 1, ledger: [
            new PointsAward(PointsReason.Workout, 5, D(0), "workout:2026-10-05"),
            new PointsAward(PointsReason.Workout, 5, D(1), "workout:2026-10-06")]);
        Assert.Equal(5, stats[0].TotalPoints);
    }
}
