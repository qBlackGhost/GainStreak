using GymApp.Core;

namespace GymApp.Tests;

public class StreakCalculatorTests
{
    // 2026-10-05 is a Monday. D(0) = Mon ... D(6) = Sun
    private static readonly DateOnly Mon = new(2026, 10, 5);
    private static DateOnly D(int offset) => Mon.AddDays(offset);

    // Upper/Lower: Mon W, Tue W, Wed R, Thu W, Fri W, Sat R, Sun R
    private static readonly ScheduleChange[] UpperLower = { new ScheduleChange(Mon, "WWRWWRR") };

    private static StreakResult Run(int[] logged, int today, IReadOnlyList<ScheduleChange>? schedule = null) =>
        StreakCalculator.Calculate(logged.Select(D), schedule ?? UpperLower, D(today));

    [Fact] public void NoWorkouts_NotStarted() =>
        Assert.Equal(new StreakResult(0, 0, StreakStatus.NotStarted), Run([], 2));

    [Fact] public void FirstWorkoutToday_StartsAtOne() =>
        Assert.Equal(new StreakResult(1, 1, StreakStatus.Active), Run([0], 0));

    [Fact] public void RestDays_CountTowardTheStreak() =>
        // Mon, Tue logged, Wed is a rest day -> 3
        Assert.Equal(new StreakResult(3, 3, StreakStatus.Active), Run([0, 1], 2));

    [Fact] public void RestDaysBeforeTheFirstWorkout_DoNotCount() =>
        // first workout on Friday, then Sat + Sun rest -> 3
        Assert.Equal(new StreakResult(3, 3, StreakStatus.Active), Run([4], 6));

    [Fact] public void MissedTrainingDay_BreaksTheStreak() =>
        // Mon, Tue, Wed(rest) = 3, Thu missed, today is Fri (pending)
        Assert.Equal(new StreakResult(0, 3, StreakStatus.Broken), Run([0, 1], 4));

    [Fact] public void TodayPending_KeepsTheStreakAlive() =>
        // Mon, Tue, Wed(rest), today is Thu with no workout yet
        Assert.Equal(new StreakResult(3, 3, StreakStatus.PendingToday), Run([0, 1], 3));

    [Fact] public void ExtraWorkoutOnRestDay_CountsOnce() =>
        Assert.Equal(new StreakResult(3, 3, StreakStatus.Active), Run([0, 1, 2], 2));

    [Fact] public void RestDaysAfterABreak_DoNotRestartTheStreak() =>
        // Mon, Tue, Wed(rest), Thu = 4, Fri missed, Sat + Sun rest
        Assert.Equal(new StreakResult(0, 4, StreakStatus.Broken), Run([0, 1, 3], 6));

    [Fact] public void WorkoutAfterABreak_StartsAFreshStreak() =>
        // same as above, then an extra workout on Sunday
        Assert.Equal(new StreakResult(1, 4, StreakStatus.Active), Run([0, 1, 3, 6], 6));

    [Fact] public void ScheduleChange_DoesNotRewriteThePast()
    {
        // Wednesday was a rest day under the old schedule; from Thursday every day is a training day
        var schedule = new[] { new ScheduleChange(Mon, "WWRWWRR"), new ScheduleChange(D(3), "WWWWWWW") };
        Assert.Equal(new StreakResult(4, 4, StreakStatus.Active), Run([0, 1, 3], 3, schedule));
    }

    [Fact] public void FirstScheduleEntry_AlsoCoversEarlierDays()
    {
        var schedule = new[] { new ScheduleChange(D(4), "WWRWWRR") };
        Assert.Equal(new StreakResult(3, 3, StreakStatus.Active), Run([0, 1], 2, schedule));
    }

    [Fact] public void NoSchedule_EveryDayIsATrainingDay() =>
        // Mon logged, Tue missed -> broken
        Assert.Equal(new StreakResult(0, 1, StreakStatus.Broken), Run([0], 2, Array.Empty<ScheduleChange>()));

    [Fact] public void FutureLogs_AreIgnored() =>
        Assert.Equal(new StreakResult(0, 0, StreakStatus.NotStarted), Run([9], 2));

    [Fact] public void InvalidPattern_Throws() =>
        Assert.Throws<ArgumentException>(() => Run([0], 1, new[] { new ScheduleChange(Mon, "WWW") }));

    [Fact] public void GraceDays_NotSupportedYet() =>
        Assert.Throws<NotSupportedException>(() =>
            StreakCalculator.Calculate(new[] { Mon }, UpperLower, D(1), graceDays: 1));

    [Fact(Skip = "Grace rule not implemented yet")]
    public void Grace_MissedDayCanBeMadeUpTheNextDay()
    {
        // Mon logged, Tue (training) missed, today Wed: still saveable with a workout today
        var r = StreakCalculator.Calculate(new[] { Mon }, UpperLower, D(2), graceDays: 1);
        Assert.Equal(StreakStatus.AtRisk, r.Status);
    }

    // WeekPattern

    [Fact] public void WeekPattern_IsMondayFirst_RegardlessOfStartDay() =>
        // PPL starting Wednesday: Mon-Fri workout, Sat rest, Sun workout
        Assert.Equal("WWWWWRW", WeekPattern.From(WeekSchedule.Build("WWWRWWW", DayOfWeek.Wednesday, 3)));

    [Fact] public void WeekPattern_FromCustomWeekdays() =>
        Assert.Equal("WRWRWRR",
            WeekPattern.From(WeekSchedule.FromWeekdays(new[] { DayOfWeek.Friday, DayOfWeek.Monday, DayOfWeek.Wednesday })));
}
