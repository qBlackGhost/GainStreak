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

    private static readonly ScheduleChange[] None = Array.Empty<ScheduleChange>();

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

    [Fact] public void MissedTrainingDayNotMadeUp_BreaksTheStreak() =>
        // Mon = 1, Tue missed (debt), Wed rest not logged -> break, today Thu (training) not logged yet
        Assert.Equal(new StreakResult(0, 1, StreakStatus.Broken), Run([0], 3));

    [Fact] public void TodayPending_KeepsTheStreakAlive() =>
        // Mon, Tue, Wed(rest), today is Thu with no workout yet
        Assert.Equal(new StreakResult(3, 3, StreakStatus.PendingToday), Run([0, 1], 3));

    [Fact] public void ExtraWorkoutOnRestDay_CountsOnce() =>
        Assert.Equal(new StreakResult(3, 3, StreakStatus.Active), Run([0, 1, 2], 2));

    [Fact] public void RestDaysAfterABreak_CountAgain() =>
        // Mon, Tue, Wed(rest), Thu = 4, Fri missed (debt), Sat rest not logged -> break, Sun rest -> 1
        Assert.Equal(new StreakResult(1, 4, StreakStatus.Active), Run([0, 1, 3], 6));

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
        // Mon logged, Tue missed (debt), Wed missed -> break, today Thu pending
        Assert.Equal(new StreakResult(0, 1, StreakStatus.Broken), Run([0], 3, None));

    [Fact] public void FutureLogs_AreIgnored() =>
        Assert.Equal(new StreakResult(0, 0, StreakStatus.NotStarted), Run([9], 2));

    [Fact] public void InvalidPattern_Throws() =>
        Assert.Throws<ArgumentException>(() => Run([0], 1, new[] { new ScheduleChange(Mon, "WWW") }));

    // Grace rule (shift model). PPL: Mon W, Tue W, Wed W, Thu R, Fri W, Sat W, Sun W

    private static readonly ScheduleChange[] Ppl = { new ScheduleChange(Mon, "WWWRWWW") };

    [Fact] public void Grace_MissAbsorbedByLoggedRestDay() =>
        // example A: Mon 1, Tue missed 2 (debt), Wed 3 (shifted), Thu rest logged 4 (paid)
        Assert.Equal(new StreakResult(4, 4, StreakStatus.Active), Run([0, 2, 3], 3, Ppl));

    [Fact] public void Grace_ShiftChainAcrossSeveralTrainingDays_EndingInLoggedRestDay() =>
        // Mon-Wed, Thu rest, Fri missed, Sat..next Wed logged (shifted each day), next Thu rest logged
        Assert.Equal(new StreakResult(11, 11, StreakStatus.Active), Run([0, 1, 2, 5, 6, 7, 8, 9, 10], 10, Ppl));

    [Fact] public void Grace_ShiftChainEndingInUnloggedRestDay_Breaks() =>
        // example B: Mon 1, Tue missed, Wed 3 (shifted), Thu rest not logged -> break, Fri 1
        Assert.Equal(new StreakResult(1, 1, StreakStatus.Active), Run([0, 2, 4], 4, Ppl));

    [Fact] public void Grace_TwoConsecutiveMisses_Break() =>
        // example C: Mon 1, Tue missed, Wed missed -> break, Thu rest 1
        Assert.Equal(new StreakResult(1, 1, StreakStatus.Active), Run([0], 3, Ppl));

    [Fact] public void Grace_MissYesterday_NothingToday_IsAtRisk() =>
        // example D: Mon 1, Tue missed 2, today Wed not logged
        Assert.Equal(new StreakResult(2, 2, StreakStatus.AtRisk), Run([0], 2, Ppl));

    [Fact] public void Grace_MissYesterday_LoggedTodayOnTrainingDay_IsShifted() =>
        // example E: Mon 1, Tue missed 2, today Wed logged 3, but Wed's training is now owed
        Assert.Equal(new StreakResult(3, 3, StreakStatus.Shifted), Run([0, 2], 2, Ppl));

    [Fact] public void Grace_TrainingDayToday_NoDebt_IsTodayPending() =>
        // example F: count runs through yesterday
        Assert.Equal(new StreakResult(2, 2, StreakStatus.PendingToday), Run([0, 1], 2, Ppl));

    [Fact] public void Grace_WorkoutOnRestDayWithoutDebt_HasNoSpecialEffect()
    {
        Assert.Equal(Run([0, 1, 2], 3, Ppl), Run([0, 1, 2, 3], 3, Ppl));
        // it doesn't pre-pay a later miss either: Fri missed, today Sat not logged
        Assert.Equal(Run([0, 1, 2], 5, Ppl), Run([0, 1, 2, 3], 5, Ppl));
        Assert.Equal(StreakStatus.AtRisk, Run([0, 1, 2, 3], 5, Ppl).Status);
    }

    [Fact] public void Grace_MissBeforeScheduleChange_UsesNewScheduleForNextDay()
    {
        // example G: Tue (PPL training) missed; from Wed Upper/Lower applies, where Wed is a rest day
        var schedule = new[] { new ScheduleChange(Mon, "WWWRWWW"), new ScheduleChange(D(2), "WWRWWRR") };
        Assert.Equal(new StreakResult(3, 3, StreakStatus.Active), Run([0, 2], 2, schedule));
        // under PPL alone Wed would be a training day, so the same logs leave the schedule shifted
        Assert.Equal(StreakStatus.Shifted, Run([0, 2], 2, Ppl).Status);
    }

    [Fact] public void Grace_NoLogsAtAll_IsNoneWithZero() =>
        // a schedule with rest days doesn't start a streak on its own
        Assert.Equal(new StreakResult(0, 0, StreakStatus.NotStarted), Run([], 6, Ppl));

    [Fact] public void Grace_RestDaysOnly_WithoutLogs_StillCount()
    {
        // Mon workout, then six unlogged rest days -> 7
        var schedule = new[] { new ScheduleChange(Mon, "WRRRRRR") };
        Assert.Equal(new StreakResult(7, 7, StreakStatus.Active), Run([0], 6, schedule));
    }

    // WeekPattern

    [Fact] public void WeekPattern_IsMondayFirst_RegardlessOfStartDay() =>
        // PPL starting Wednesday: Mon-Fri workout, Sat rest, Sun workout
        Assert.Equal("WWWWWRW", WeekPattern.From(WeekSchedule.Build("WWWRWWW", DayOfWeek.Wednesday, 3)));

    [Fact] public void WeekPattern_FromCustomWeekdays() =>
        Assert.Equal("WRWRWRR",
            WeekPattern.From(WeekSchedule.FromWeekdays(new[] { DayOfWeek.Friday, DayOfWeek.Monday, DayOfWeek.Wednesday })));
}
