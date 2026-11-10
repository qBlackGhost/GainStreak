using GymApp.Core;

namespace GymApp.Tests;

/// <summary>Frozen (vacation) days in the streak replay, the timeline and the week bonus (0.19.0).</summary>
public class VacationStreakTests
{
    // 2026-10-05 is a Monday. D(0) = Mon ... D(6) = Sun
    private static readonly DateOnly Mon = new(2026, 10, 5);
    private static DateOnly D(int n) => Mon.AddDays(n);

    // Upper/Lower: Mon W, Tue W, Wed R, Thu W, Fri W, Sat R, Sun R
    private static readonly ScheduleChange[] UpperLower = [new(Mon, "WWRWWRR")];
    // PPL: Mon W, Tue W, Wed W, Thu R, Fri W, Sat W, Sun W
    private static readonly ScheduleChange[] Ppl = [new(Mon, "WWWRWWW")];
    private static readonly Dictionary<DateOnly, MissChoice> NoChoices = new();

    private static int[] Days(int from, int to) => Enumerable.Range(from, to - from + 1).ToArray();

    /// <summary>The 0.18.1 rule from Monday, with the given verified workouts and vacation days.</summary>
    private static RestDayRule Rule(int[] frozen, params int[] verified) =>
        new(Mon, verified.Select(D).ToHashSet(), frozen.Select(D).ToHashSet());

    private static StreakResult Run(int[] logged, int today, RestDayRule rule, IReadOnlyList<ScheduleChange>? schedule = null) =>
        StreakCalculator.Calculate(logged.Select(D), schedule ?? UpperLower, NoChoices, D(today), rule);

    private static IReadOnlyList<TrainingDayOutcome> Timeline(int[] logged, int today, RestDayRule rule,
        IReadOnlyList<ScheduleChange>? schedule = null, int from = 0, int until = 6) =>
        StreakCalculator.Timeline(logged.Select(D), schedule ?? UpperLower, NoChoices, D(today), D(from), D(until), rule);

    // The streak

    [Fact] public void FrozenTrainingDays_KeepTheStreak_OnIce_WithTheReturnDate() =>
        // Mon, Tue logged, away Wed to Fri (back Sat): Thu and Fri are training days, nothing is owed
        Assert.Equal(new StreakResult(2, 2, StreakStatus.Active, Stale: true, VacationUntil: D(5)),
            Run([0, 1], 4, Rule(Days(2, 4))));

    [Fact] public void AfterTheVacation_NoMissIsOwed() =>
        // back on Sat; Sat and Sun rest; next Mon is just today's training
        Assert.Equal(new StreakResult(2, 2, StreakStatus.PendingToday), Run([0, 1], 7, Rule(Days(2, 4))));

    [Fact] public void AfterTheVacation_TheNextWorkoutAdds() =>
        Assert.Equal(new StreakResult(3, 3, StreakStatus.Active), Run([0, 1, 7], 7, Rule(Days(2, 4))));

    [Fact] public void VerifiedWorkoutOnAFrozenDay_Adds() =>
        Assert.Equal(new StreakResult(3, 3, StreakStatus.Active, VacationUntil: D(5)), Run([0, 1, 3], 3, Rule(Days(2, 4), 3)));

    [Fact] public void UnverifiedWorkoutOnAFrozenDay_DoesNotAdd() =>
        Assert.Equal(new StreakResult(2, 2, StreakStatus.Active, Stale: true, VacationUntil: D(5)),
            Run([0, 1, 3], 3, Rule(Days(2, 4))));

    [Fact] public void FrozenDays_NeverAskForAChoice()
    {
        // Thu and Fri missed while away; today is Fri, still away
        var r = Run([0, 1], 4, Rule(Days(3, 4)));
        Assert.Equal(StreakStatus.Active, r.Status);
        Assert.Null(r.OwedDay);
    }

    [Fact] public void TheFirstFrozenDay_ClosesAShiftChain()
    {
        // PPL: Mon logged, Tue missed, Wed logged (pays Tue, Wed's own training is owed: Shifted). Away from Thu.
        var shifted = StreakCalculator.Calculate([D(0), D(2)], Ppl, NoChoices, D(2), Rule([]));
        Assert.Equal(StreakStatus.Shifted, shifted.Status);

        var r = Run([0, 2], 4, Rule(Days(3, 5)), Ppl);
        Assert.Equal(new StreakResult(3, 3, StreakStatus.Active, Stale: true, VacationUntil: D(6)), r);

        var t = Timeline([0, 2], 4, Rule(Days(3, 5)), Ppl);
        Assert.Equal(DayOutcome.Graced, t.Single(x => x.Date == D(2)).Outcome);
        Assert.Equal(DayOutcome.Frozen, t.Single(x => x.Date == D(4)).Outcome);
    }

    [Fact] public void WithNoStreak_OnlyAVerifiedWorkoutStartsOne()
    {
        Assert.Equal(new StreakResult(0, 0, StreakStatus.NotStarted), Run([2], 3, Rule(Days(2, 3))));
        Assert.Equal(new StreakResult(1, 1, StreakStatus.Active, Stale: true, VacationUntil: D(4)), Run([2], 3, Rule(Days(2, 3), 2)));
    }

    [Fact] public void EndingEarly_UnfreezesTheNextDay()
    {
        // away from Wed, back early on Thu: Fri is a training day again
        var frozen = VacationRules.FrozenDays([new VacationPeriod(D(2), D(7), D(3))]).Select(d => d.DayNumber - Mon.DayNumber).ToArray();
        Assert.Equal(new StreakResult(2, 2, StreakStatus.PendingToday), Run([0, 1], 4, Rule(frozen)));
    }

    [Fact] public void NoVacation_ChangesNothing()
    {
        int[] logged = [0, 1, 3];
        var without = StreakCalculator.Calculate(logged.Select(D), UpperLower, NoChoices, D(6), new RestDayRule(Mon, new HashSet<DateOnly>()));
        Assert.Equal(without, Run(logged, 6, Rule([])));
    }

    [Fact] public void LastBreak_IgnoresFrozenDays() =>
        Assert.Null(StreakCalculator.LastBreak([D(0), D(1), D(7)], UpperLower, NoChoices, D(7), Rule(Days(2, 6))));

    // The timeline and the week row

    [Fact] public void Timeline_FrozenTrainingDays_PastAndFuture()
    {
        // away Thu to Sun, today Thu: Thu and Fri are Frozen; a verified Thu would be Done
        Assert.Equal([DayOutcome.Done, DayOutcome.Done, DayOutcome.Frozen, DayOutcome.Frozen],
            Timeline([0, 1], 3, Rule(Days(3, 6))).Select(t => t.Outcome));
        Assert.Equal([DayOutcome.Done, DayOutcome.Done, DayOutcome.Done, DayOutcome.Frozen],
            Timeline([0, 1, 3], 3, Rule(Days(3, 6), 3)).Select(t => t.Outcome));
    }

    [Fact] public void WeekRow_ShowsVacationDays_RestDaysAmongThemToo()
    {
        var rule = Rule(Days(4, 6));   // away Fri to Sun; Sat and Sun are rest days under Upper/Lower
        var kinds = WeekRow.Build(Timeline([0, 1, 3], 4, rule), D(4), rule.Frozen).Select(c => c.Kind).ToArray();
        Assert.Equal([WeekChipKind.Done, WeekChipKind.Done, WeekChipKind.Rest, WeekChipKind.Done,
            WeekChipKind.Vacation, WeekChipKind.Vacation, WeekChipKind.Vacation], kinds);
    }

    // The week bonus

    private static IReadOnlyList<PointsAward> Weeks(int[] logged, int today, RestDayRule rule)
    {
        var dates = logged.Select(D).ToList();
        var timeline = StreakCalculator.Timeline(dates, UpperLower, NoChoices, D(today), Mon,
            PointsCalculator.MondayOf(D(today)).AddDays(6), rule);
        return PointsCalculator.Calculate(dates, timeline, Mon, [], D(today), Mon)
            .Where(a => a.Reason is PointsReason.WeekComplete or PointsReason.WeekCompleteGrace).ToList();
    }

    [Fact] public void WeekBonus_JudgedOnTheOtherTrainingDays()
    {
        // Mon, Tue done, away Thu and Fri
        var w = Assert.Single(Weeks([0, 1], 7, Rule(Days(3, 4))));
        Assert.Equal(PointsReason.WeekComplete, w.Reason);
        Assert.Equal(D(1), w.Date);
    }

    [Fact] public void WeekBonus_NoneForAWeekWithOnlyFrozenTrainingDays()
    {
        // week 1 done; away the whole of week 2's training days (Mon to Fri)
        var w = Assert.Single(Weeks([0, 1, 3, 4], 14, Rule(Days(7, 11))));
        Assert.Equal(D(4), w.Date);
    }

    [Fact] public void WeekBonus_HandBuiltTimeline_FrozenLeftOut()
    {
        TrainingDayOutcome[] t = [new(D(0), DayOutcome.Done), new(D(1), DayOutcome.Frozen)];
        Assert.Equal(PointsReason.WeekComplete,
            Assert.Single(PointsCalculator.Calculate([D(0)], t, Mon, [], D(7), Mon), a => a.Reason != PointsReason.Workout).Reason);
        Assert.DoesNotContain(PointsCalculator.Calculate([], [new(D(1), DayOutcome.Frozen)], Mon, [], D(7), Mon),
            a => a.Reason is PointsReason.WeekComplete or PointsReason.WeekCompleteGrace);
    }
}
