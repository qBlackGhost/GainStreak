using GymApp.Core;

namespace GymApp.Tests;

public class StreakCalculatorTests
{
    // 2026-10-05 is a Monday. D(0) = Mon ... D(6) = Sun
    private static readonly DateOnly Mon = new(2026, 10, 5);
    private static DateOnly D(int offset) => Mon.AddDays(offset);

    // Upper/Lower: Mon W, Tue W, Wed R, Thu W, Fri W, Sat R, Sun R
    private static readonly ScheduleChange[] UpperLower = { new ScheduleChange(Mon, "WWRWWRR") };

    private static StreakResult Run(int[] logged, int today, IReadOnlyList<ScheduleChange>? schedule = null,
        IReadOnlyDictionary<DateOnly, MissChoice>? choices = null) =>
        StreakCalculator.Calculate(logged.Select(D), schedule ?? UpperLower, choices ?? NoChoices, D(today));

    private static readonly Dictionary<DateOnly, MissChoice> NoChoices = new();

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

    [Fact] public void RestDaysAfterABreak_DoNotCountUntilTheNextWorkout() =>
        // Mon, Tue, Wed(rest), Thu = 4, Fri missed (debt), Sat rest not logged -> break, Sun rest stays 0
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
        // example C: Mon 1, Tue missed, Wed missed -> break, Thu rest stays 0 (no streak, rest days don't count)
        Assert.Equal(new StreakResult(0, 1, StreakStatus.Broken), Run([0], 3, Ppl));

    [Fact] public void Grace_MissYesterday_NothingToday_IsAtRisk() =>
        // example D: Mon 1, Tue missed 2 (made up before the rest day), today Wed not logged
        Assert.Equal(new StreakResult(2, 2, StreakStatus.AtRisk, D(1)),
            Run([0], 2, Ppl, new Dictionary<DateOnly, MissChoice> { [D(1)] = MissChoice.ShiftToRest }));

    [Fact] public void Grace_MissYesterday_LoggedTodayOnTrainingDay_IsShifted() =>
        // example E: Mon 1, Tue missed 2, today Wed logged 3, but Wed's training is now owed
        Assert.Equal(new StreakResult(3, 3, StreakStatus.Shifted, D(2)), Run([0, 2], 2, Ppl));

    [Fact] public void Grace_TrainingDayToday_NoDebt_IsTodayPending() =>
        // example F: count runs through yesterday
        Assert.Equal(new StreakResult(2, 2, StreakStatus.PendingToday), Run([0, 1], 2, Ppl));

    [Fact] public void Grace_WorkoutOnRestDayWithoutDebt_HasNoSpecialEffect()
    {
        Assert.Equal(Run([0, 1, 2], 3, Ppl), Run([0, 1, 2, 3], 3, Ppl));
        // it doesn't pre-pay a later miss either: Fri missed, today Sat not logged
        Assert.Equal(Run([0, 1, 2], 5, Ppl), Run([0, 1, 2, 3], 5, Ppl));
        Assert.Equal(StreakStatus.NeedsChoice, Run([0, 1, 2, 3], 5, Ppl).Status);
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

    // Timeline (Done / Graced / Missed / Pending per training day)

    private static string Timeline(int[] logged, int today, IReadOnlyList<ScheduleChange> schedule, int from = 0, int until = 6,
        IReadOnlyDictionary<DateOnly, MissChoice>? choices = null) =>
        string.Join(" ", StreakCalculator.Timeline(logged.Select(D), schedule, choices ?? NoChoices, D(today), D(from), D(until))
            .Select(t => $"{t.Date.Day}:{t.Outcome}"));

    [Fact] public void Timeline_AllLogged_IsDone() =>
        Assert.Equal("5:Done 6:Done 8:Done 9:Done", Timeline([0, 1, 3, 4], 6, UpperLower));

    [Fact] public void Timeline_MissPaidByARestDayWorkout_IsGraced() =>
        Assert.Equal("5:Done 6:Graced 8:Done 9:Done", Timeline([0, 2, 3, 4], 6, UpperLower));

    [Fact] public void Timeline_ShiftChain_GracesEachShiftedDay() =>
        // PPL: Tue missed, Wed pays it (its own training shifts), Thu rest pays Wed
        Assert.Equal("5:Done 6:Graced 7:Graced 9:Done 10:Done 11:Done", Timeline([0, 2, 3, 4, 5, 6], 6, Ppl));

    [Fact] public void Timeline_ChainEndingInAnUnloggedRestDay_IsMissed() =>
        // PPL: Tue paid by Wed, but Thu (rest) isn't logged, so Wed's shifted training is missed; today Fri pending
        Assert.Equal("5:Done 6:Graced 7:Missed 9:Pending 10:Pending 11:Pending", Timeline([0, 2], 4, Ppl));

    [Fact] public void Timeline_TwoMissesInARow_AreBothMissed() =>
        Assert.Equal("5:Done 6:Missed 7:Missed 9:Pending 10:Pending 11:Pending", Timeline([0], 4, Ppl));

    [Fact] public void Timeline_OwedDay_StaysPendingUntilPaid() =>
        // Upper/Lower: Tue missed, today Wed not logged yet (AtRisk)
        Assert.Equal("5:Done 6:Pending 8:Pending 9:Pending", Timeline([0], 2, UpperLower));

    [Fact] public void Timeline_ShiftedToday_IsPending() =>
        // PPL: Tue missed, Wed logged today: Tue is paid, Wed's own training is owed
        Assert.Equal("5:Done 6:Graced 7:Pending", Timeline([0, 2], 2, Ppl, until: 2));

    [Fact] public void Timeline_DaysBeforeTheFirstWorkout_AreMissed_AndFutureDaysPending() =>
        Assert.Equal("5:Missed 6:Missed 8:Done 9:Pending", Timeline([3], 3, UpperLower));

    [Fact] public void Timeline_NoLogs_PastDaysMissed() =>
        Assert.Equal("5:Missed 6:Missed 8:Pending 9:Pending", Timeline([], 3, UpperLower));

    [Fact] public void Timeline_MatchesCalculate()
    {
        // the same replay: a broken streak shows a Missed day, a shifted one a Pending day
        Assert.Equal(StreakStatus.Shifted, Run([0, 2], 2, Ppl).Status);
        Assert.Contains("Missed", Timeline([0, 2], 4, Ppl));
        Assert.Equal(1, Run([0, 2, 4], 4, Ppl).Current);
    }

    // Missed-day choices (Phase 12a). PPL from Mon; the examples are the ones in PHASE_12_POLISH.md

    private static Dictionary<DateOnly, MissChoice> Chose(int missed, MissChoice choice) => new() { [D(missed)] = choice };

    // What ShiftWeek stores: PPL moved one day later from Wed, so Fri is the rest day
    private static readonly ScheduleChange[] PplShiftedFromWed =
        { new ScheduleChange(Mon, "WWWRWWW"), new ScheduleChange(D(2), "WWWWRWW") };

    [Fact] public void Choice_A_ShiftWeek_PaidToday_IsActive()
    {
        // Mon 1, Tue missed 2, Wed: ShiftWeek, logged 3. Nothing more is owed
        var choices = Chose(1, MissChoice.ShiftWeek);
        Assert.Equal(new StreakResult(3, 3, StreakStatus.Active), Run([0, 2], 2, PplShiftedFromWed, choices));
        // Thu is now a training day (Legs), Fri the rest day
        Assert.Equal(new StreakResult(5, 5, StreakStatus.Active), Run([0, 2, 3], 4, PplShiftedFromWed, choices));
    }

    [Fact] public void Choice_B_ShiftToRest_ChainEndsOnTheRestDay()
    {
        var choices = Chose(1, MissChoice.ShiftToRest);
        // Wed: logged 3, Wed's own training is owed
        Assert.Equal(new StreakResult(3, 3, StreakStatus.Shifted, D(2)), Run([0, 2], 2, Ppl, choices));
        // Thu (rest) logged 4, paid
        Assert.Equal(new StreakResult(4, 4, StreakStatus.Active), Run([0, 2, 3], 3, Ppl, choices));
    }

    [Fact] public void Choice_C_Continue_ResetsNow_AndTodayStartsANewStreak()
    {
        var choices = Chose(1, MissChoice.Continue);
        Assert.Equal(new StreakResult(0, 1, StreakStatus.Broken), Run([0], 2, Ppl, choices));
        Assert.Equal(new StreakResult(1, 1, StreakStatus.Active), Run([0, 2], 2, Ppl, choices));
    }

    [Fact] public void Choice_D_NotChosenYet_NeedsChoice() =>
        Assert.Equal(new StreakResult(2, 2, StreakStatus.NeedsChoice, D(1)), Run([0], 2, Ppl));

    [Fact] public void Choice_D_ChosenButNotLoggedYet_IsAtRisk()
    {
        Assert.Equal(new StreakResult(2, 2, StreakStatus.AtRisk, D(1)), Run([0], 2, Ppl, Chose(1, MissChoice.ShiftToRest)));
        Assert.Equal(new StreakResult(2, 2, StreakStatus.AtRisk, D(1)), Run([0], 2, PplShiftedFromWed, Chose(1, MissChoice.ShiftWeek)));
    }

    [Fact] public void Choice_E_ChoseButDidNotTrain_BreaksTheNextDay() =>
        // Wed: ShiftWeek, not logged. On Thu Wed is consumed by the break; Thu (Legs after the shift) not logged yet
        Assert.Equal(new StreakResult(0, 1, StreakStatus.Broken), Run([0], 3, PplShiftedFromWed, Chose(1, MissChoice.ShiftWeek)));

    [Fact] public void Choice_F_InactiveUserWithoutSchedule_StaysAtZero()
    {
        // last workout 1 Oct: 2 Oct pending, 3 Oct NeedsChoice (a streak is still at stake), then 0 every day
        Assert.Equal(StreakStatus.PendingToday, Run([-4], -3, None).Status);
        Assert.Equal(new StreakResult(2, 2, StreakStatus.NeedsChoice, D(-3)), Run([-4], -2, None));
        for (var today = -1; today <= 20; today++)
            Assert.Equal(new StreakResult(0, 1, StreakStatus.Broken), Run([-4], today, None));
    }

    [Fact] public void Choice_G_InactiveUser_RestDaysAfterABreakStayAtZero()
    {
        // Mon 1, Tue missed, Wed missed -> break; Thu (rest) and every later day stay 0
        for (var today = 3; today <= 20; today++)
            Assert.Equal(new StreakResult(0, 1, StreakStatus.Broken), Run([0], today, Ppl));
    }

    [Fact] public void Choice_G_AWorkoutAfterABreak_StartsCountingAgain() =>
        // the break on Wed, then Fri logged 1, Sat 2, Sun (training) pending today
        Assert.Equal(new StreakResult(2, 2, StreakStatus.PendingToday), Run([0, 4, 5], 6, Ppl));

    [Fact] public void Choice_H_OldMissWithoutAChoice_ActsLikeShiftToRest() =>
        Assert.Equal(Run([0, 2], 2, Ppl, Chose(1, MissChoice.ShiftToRest)), Run([0, 2], 2, Ppl));

    [Fact] public void Choice_OnlyTheFreshMissIsAsked_NotADayOwedInsideAChain() =>
        // Tue missed, Wed paid it (Wed owed), today Thu (rest) not logged: owed inside the chain, so AtRisk
        Assert.Equal(new StreakResult(3, 3, StreakStatus.AtRisk, D(2)), Run([0, 2], 3, Ppl));

    [Fact] public void Choice_ShiftToRestChain_AcrossSeveralTrainingDays_EndingInALoggedRestDay_KeepsTheStreak() =>
        // Fri missed (ShiftToRest), Sat..next Wed each pay the day before, next Thu (rest) logged
        Assert.Equal(new StreakResult(11, 11, StreakStatus.Active),
            Run([0, 1, 2, 5, 6, 7, 8, 9, 10], 10, Ppl, Chose(4, MissChoice.ShiftToRest)));

    [Fact] public void Choice_ContinueInsideAChain_IsIgnored() =>
        // only a fresh miss takes a choice; a stray row for Wed (owed inside the chain) changes nothing
        Assert.Equal(Run([0, 2, 3], 3, Ppl), Run([0, 2, 3], 3, Ppl, Chose(2, MissChoice.Continue)));

    [Fact] public void Timeline_Continue_MakesTheOwedDayMissed() =>
        Assert.Equal("5:Done 6:Missed 7:Done", Timeline([0, 2], 3, Ppl, until: 2, choices: Chose(1, MissChoice.Continue)));

    [Fact] public void Timeline_ShiftWeek_GracesTheOwedDay_AndTheDayThatPaysIsDone() =>
        Assert.Equal("5:Done 6:Graced 7:Done", Timeline([0, 2], 2, PplShiftedFromWed, until: 2, choices: Chose(1, MissChoice.ShiftWeek)));

    [Fact] public void Timeline_ShiftToRest_GracesTheOwedDayOncePaid() =>
        Assert.Equal("5:Done 6:Graced 7:Graced", Timeline([0, 2, 3], 3, Ppl, until: 2, choices: Chose(1, MissChoice.ShiftToRest)));

    // Week row (dashboard)

    [Fact] public void WeekRow_MidWeek_ShowsEveryKindOfChip()
    {
        // PPL, today Sat: Mon done, Tue missed and made up Wed, Wed made up on Thu (rest, logged),
        // Fri missed and still owed, Sat (today) and Sun upcoming
        var logged = new[] { 0, 2, 3 }.Select(D).ToList();
        var timeline = StreakCalculator.Timeline(logged, Ppl, NoChoices, D(5), D(0), D(6));
        var row = WeekRow.Build(timeline, D(5));

        Assert.Equal(Enumerable.Range(0, 7).Select(D), row.Select(c => c.Date));
        Assert.Equal([WeekChipKind.Done, WeekChipKind.MadeUp, WeekChipKind.MadeUp, WeekChipKind.Rest,
                      WeekChipKind.Missed, WeekChipKind.Upcoming, WeekChipKind.Upcoming], row.Select(c => c.Kind));
        Assert.Equal([D(5)], row.Where(c => c.IsToday).Select(c => c.Date));
    }

    [Fact] public void WeekRow_SettledMiss_IsMissed() =>
        // Upper/Lower, today Sun: Mon done, Tue missed and the Wed rest day not logged -> broken
        Assert.Equal([WeekChipKind.Done, WeekChipKind.Missed, WeekChipKind.Rest, WeekChipKind.Missed, WeekChipKind.Missed,
                      WeekChipKind.Rest, WeekChipKind.Rest],
            WeekRow.Build(StreakCalculator.Timeline([D(0)], UpperLower, NoChoices, D(6), D(0), D(6)), D(6)).Select(c => c.Kind));

    // Rest days under the 0.18.1 rule: they keep the streak without adding, unless a verified workout is logged

    private static RestDayRule Rule(int from, params int[] verified) => new(D(from), verified.Select(D).ToHashSet());

    private static StreakResult RunRule(int[] logged, int today, RestDayRule rule, IReadOnlyList<ScheduleChange>? schedule = null) =>
        StreakCalculator.Calculate(logged.Select(D), schedule ?? UpperLower, NoChoices, D(today), rule);

    [Fact] public void RestRule_RestDayKeepsTheStreak_WithoutAdding_AndIsStale() =>
        // Mon, Tue logged, today Wed (rest): still 2, on ice
        Assert.Equal(new StreakResult(2, 2, StreakStatus.Active, Stale: true), RunRule([0, 1], 2, Rule(0)));

    [Fact] public void RestRule_RestDaysBeforeTheRule_StillCount() =>
        // the rule starts Fri: Wed (rest) counts, Sat and Sun don't -> Mon, Tue, Wed, Thu, Fri = 5 on Sun
        Assert.Equal(new StreakResult(5, 5, StreakStatus.Active, Stale: true), RunRule([0, 1, 3, 4], 6, Rule(4)));

    [Fact] public void RestRule_UnverifiedWorkoutOnARestDay_DoesNotAdd() =>
        Assert.Equal(new StreakResult(2, 2, StreakStatus.Active, Stale: true), RunRule([0, 1, 2], 2, Rule(0)));

    [Fact] public void RestRule_VerifiedWorkoutOnARestDay_Adds_AndIsNotStale() =>
        Assert.Equal(new StreakResult(3, 3, StreakStatus.Active), RunRule([0, 1, 2], 2, Rule(0, 2)));

    [Fact] public void RestRule_VerifiedTrainingDayWorkouts_CountAsBefore() =>
        Assert.Equal(new StreakResult(2, 2, StreakStatus.Active), RunRule([0, 1], 1, Rule(0, 0, 1)));

    [Fact] public void RestRule_TrainingDayAfterRest_ContinuesFromTheFrozenCount() =>
        // Mon, Tue, Wed rest (no add), Thu logged -> 3
        Assert.Equal(new StreakResult(3, 3, StreakStatus.Active), RunRule([0, 1, 3], 3, Rule(0)));

    [Fact] public void RestRule_PendingTrainingDayAfterRest_IsNotStale() =>
        Assert.Equal(new StreakResult(2, 2, StreakStatus.PendingToday), RunRule([0, 1], 3, Rule(0)));

    [Fact] public void RestRule_MissStillBreaksOnAnUnloggedRestDay() =>
        // Mon = 1, Tue missed (debt), Wed rest not logged -> break
        Assert.Equal(new StreakResult(0, 1, StreakStatus.Broken), RunRule([0], 3, Rule(0)));

    [Fact] public void RestRule_RestDayThatPaysADebt_KeepsTheStreak_NotStale() =>
        // PPL: Mon = 1, Tue missed (2, provisional), Wed pays it (3, Wed owed), Thu rest pays Wed: 3, not on ice
        Assert.Equal(new StreakResult(3, 3, StreakStatus.Active), RunRule([0, 2, 3], 3, Rule(0), Ppl));

    [Fact] public void RestRule_VerifiedRestDayThatPaysADebt_Adds() =>
        Assert.Equal(new StreakResult(4, 4, StreakStatus.Active), RunRule([0, 2, 3], 3, Rule(0, 3), Ppl));

    [Fact] public void RestRule_UnverifiedRestDayWorkoutAlone_DoesNotStartAStreak() =>
        // the only workout is an unverified one on Wed (rest), today Wed
        Assert.Equal(new StreakResult(0, 0, StreakStatus.NotStarted), RunRule([2], 2, Rule(0)));

    [Fact] public void RestRule_VerifiedRestDayWorkout_StartsAStreak() =>
        Assert.Equal(new StreakResult(1, 1, StreakStatus.Active), RunRule([2], 2, Rule(0, 2)));

    [Fact] public void RestRule_RestDaysAfterABreak_StayAtZero_NotStale() =>
        // Mon, Tue, Wed(rest, +1 before the rule), Thu = 4, Fri missed, Sat rest not logged -> break; Sun 0, not stale
        Assert.Equal(new StreakResult(0, 4, StreakStatus.Broken), RunRule([0, 1, 3], 6, Rule(5)));
}
