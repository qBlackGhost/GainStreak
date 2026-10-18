using GymApp.Core;

namespace GymApp.Tests;

public class PointsCalculatorTests
{
    // 2026-10-05 is a Monday. D(0) = Mon ... D(6) = Sun, D(7) = next Mon
    private static readonly DateOnly Mon = new(2026, 10, 5);
    private static DateOnly D(int offset) => Mon.AddDays(offset);

    // Upper/Lower: Mon W, Tue W, Wed R, Thu W, Fri W, Sat R, Sun R
    private static readonly ScheduleChange[] UpperLower = { new ScheduleChange(Mon, "WWRWWRR") };
    // PPL: Mon W, Tue W, Wed W, Thu R, Fri W, Sat W, Sun W
    private static readonly ScheduleChange[] Ppl = { new ScheduleChange(Mon, "WWWRWWW") };

    private static readonly Dictionary<DateOnly, MissChoice> NoChoices = new();

    /// <summary>Runs the calculator on the real streak replay, like PointsService does.</summary>
    private static IReadOnlyList<PointsAward> Run(int[] logged, int today, IReadOnlyList<ScheduleChange>? schedule = null,
        CalorieRecord[]? calories = null, int windowStart = 0)
    {
        var s = schedule ?? UpperLower;
        var dates = logged.Select(D).ToList();
        var timeline = StreakCalculator.Timeline(dates, s, NoChoices, D(today), D(windowStart),
            PointsCalculator.MondayOf(D(today)).AddDays(6));
        DateOnly? start = s.Count == 0 ? null : s.Min(c => c.EffectiveFrom);
        return PointsCalculator.Calculate(dates, timeline, start, calories ?? [], D(today), D(windowStart));
    }

    private static PointsAward[] Weeks(IEnumerable<PointsAward> a) =>
        a.Where(x => x.Reason is PointsReason.WeekComplete or PointsReason.WeekCompleteGrace).ToArray();

    private static CalorieRecord Cal(int day, int consumed, int? goal = 1800) => new(D(day), consumed, goal);

    // Workouts

    [Fact] public void OneWorkoutEntryPerLogDate_NoneOnDaysWithoutALog()
    {
        var a = Run([0, 2], 3).Where(x => x.Reason == PointsReason.Workout).ToList();
        Assert.Equal(new[] { D(0), D(2) }, a.Select(x => x.Date));
        Assert.All(a, x => Assert.Equal(PointsCalculator.WorkoutPoints, x.Amount));
        Assert.Equal(new[] { "workout:2026-10-05", "workout:2026-10-07" }, a.Select(x => x.DedupKey));
    }

    [Fact] public void FutureLogs_AreIgnored() =>
        Assert.DoesNotContain(Run([0, 5], 3), x => x.Date == D(5));

    // Weeks

    [Fact] public void FullWeekDone_Gives50_DatedTheLastTrainingDay_BeforeSunday()
    {
        var w = Assert.Single(Weeks(Run([0, 1, 3, 4], 4)));   // today is Friday
        Assert.Equal(PointsReason.WeekComplete, w.Reason);
        Assert.Equal(50, w.Amount);
        Assert.Equal(D(4), w.Date);
        Assert.Equal("week:2026-10-05", w.DedupKey);
    }

    [Fact] public void FullWeek_NotYetDone_GivesNothing() =>
        Assert.Empty(Weeks(Run([0, 1, 3], 3)));   // Thursday logged, Friday still to come

    [Fact] public void GracedDay_Gives25_OnlyOnceTheWeekHasEnded()
    {
        // Tue missed, paid by a workout on Wednesday (rest day); Thu, Fri done
        int[] logs = [0, 2, 3, 4];
        Assert.Empty(Weeks(Run(logs, 5)));        // Saturday: still running
        Assert.Empty(Weeks(Run(logs, 6)));        // Sunday: still running

        var w = Assert.Single(Weeks(Run(logs, 7)));   // next Monday
        Assert.Equal(PointsReason.WeekCompleteGrace, w.Reason);
        Assert.Equal(25, w.Amount);
        Assert.Equal(D(6), w.Date);
        Assert.Equal("week:2026-10-05", w.DedupKey);
    }

    [Fact] public void GracePaymentInTheNextWeek_StillGivesTheEarlierWeekItsBonus()
    {
        // Mon and Sun are training days. Sunday missed; Monday's workout pays it, and Tuesday (rest) pays Monday
        var schedule = new[] { new ScheduleChange(Mon, "WRRRRRW") };
        Assert.Empty(Weeks(Run([0], 7, schedule)));            // Monday not logged yet: Sunday is still owed

        var w = Assert.Single(Weeks(Run([0, 7, 8], 8, schedule)));
        Assert.Equal(PointsReason.WeekCompleteGrace, w.Reason);
        Assert.Equal(D(6), w.Date);
        Assert.Equal("week:2026-10-05", w.DedupKey);
    }

    [Fact] public void MissedDay_GivesNoWeekEntry() =>
        // Fri missed, Sat (rest) not logged -> the streak breaks on Friday
        Assert.Empty(Weeks(Run([0, 1, 3], 7)));

    [Fact] public void PartialFirstWeek_GivesNoWeekEntry()
    {
        // The schedule only began on Wednesday, so the week wasn't fully scheduled
        var schedule = new[] { new ScheduleChange(D(2), "WWRWWRR") };
        Assert.Empty(Weeks(Run([0, 1, 3, 4], 7, schedule)));
    }

    [Fact] public void WeekWithNoTrainingDays_GivesNoWeekEntry()
    {
        // Sunday-only until Sunday, then Monday-only from Sunday on: this week has no training day at all
        var schedule = new[] { new ScheduleChange(Mon, "RRRRRRW"), new ScheduleChange(D(6), "WRRRRRR") };
        Assert.Empty(Weeks(Run([0], 7, schedule)));
    }

    [Fact] public void NoScheduleAtAll_GivesNoWeekEntry() =>
        Assert.Empty(Weeks(Run([0, 1, 2, 3, 4, 5, 6], 7, Array.Empty<ScheduleChange>())));

    [Fact] public void AWeekNeverGetsBoth50And25()
    {
        foreach (var a in new[] { Run([0, 1, 3, 4], 7), Run([0, 2, 3, 4], 7), Run([0, 1, 3, 4, 7, 8, 10, 11], 14) })
        {
            Assert.Equal(a.Count, a.Select(x => x.DedupKey).Distinct().Count());
            Assert.All(Weeks(a).GroupBy(x => x.DedupKey), g => Assert.Single(g));
        }
        // a fully done week stays at 50 after it ends
        Assert.Equal(PointsReason.WeekComplete, Assert.Single(Weeks(Run([0, 1, 3, 4], 7))).Reason);
    }

    [Fact] public void ScheduleChangeMidWeek_UsesTheRightPatternForEachDay()
    {
        int[] logs = [0, 1, 2, 3, 4];
        // PPL alone: Saturday and Sunday are still to come
        Assert.Empty(Weeks(Run(logs, 4, Ppl)));

        // PPL Mon-Wed, then Upper/Lower from Thursday: Thu, Fri training, the weekend rest -> done on Friday
        var schedule = new[] { new ScheduleChange(Mon, "WWWRWWW"), new ScheduleChange(D(3), "WWRWWRR") };
        var w = Assert.Single(Weeks(Run(logs, 4, schedule)));
        Assert.Equal(PointsReason.WeekComplete, w.Reason);
        Assert.Equal(D(4), w.Date);
    }

    [Fact] public void HandBuiltTimeline_GracedAndMissedDays()
    {
        DateOnly[] logs = [D(0), D(2)];
        TrainingDayOutcome[] graced = [new(D(0), DayOutcome.Done), new(D(1), DayOutcome.Graced)];
        TrainingDayOutcome[] missed = [new(D(0), DayOutcome.Done), new(D(1), DayOutcome.Missed)];

        var w = Assert.Single(Weeks(PointsCalculator.Calculate(logs, graced, Mon, [], D(7), Mon)));
        Assert.Equal(PointsReason.WeekCompleteGrace, w.Reason);
        Assert.Empty(Weeks(PointsCalculator.Calculate(logs, missed, Mon, [], D(7), Mon)));
    }

    // Calories (goal 1,800: leeway 270, so 1,530 to 2,070 counts)

    [Theory]
    [InlineData(1800, true)]
    [InlineData(1530, true)]
    [InlineData(1529, false)]
    [InlineData(2070, true)]
    [InlineData(2071, false)]
    [InlineData(0, false)]
    public void Calories_HitWithinTheLeeway(int consumed, bool hit)
    {
        var a = Run([], 3, calories: [Cal(1, consumed)]);
        Assert.Equal(hit, a.Any(x => x.Reason == PointsReason.CalorieTarget));
    }

    [Fact] public void Calories_EntryShape()
    {
        var c = Assert.Single(Run([], 3, calories: [Cal(1, 1800)]));
        Assert.Equal(new PointsAward(PointsReason.CalorieTarget, 10, D(1), "calories:2026-10-06"), c);
    }

    [Fact] public void Calories_UseTheGoalSavedWithTheLog()
    {
        // 2,250 hits a 2,000 goal (window 1,700 to 2,300) but not a 1,800 goal (1,530 to 2,070)
        Assert.Single(Run([], 3, calories: [Cal(1, 2250, 2000)]));
        Assert.Empty(Run([], 3, calories: [Cal(1, 2250, 1800)]));
    }

    [Fact] public void Calories_WithoutAGoal_GiveNothing() =>
        Assert.Empty(Run([], 3, calories: [Cal(1, 1800, null)]));

    // Window

    [Fact] public void EntriesBeforeTheWindowStart_AreNotProduced()
    {
        // a full first week, a calorie hit in it, and one workout in the second week; window starts on D(7)
        var a = Run([0, 1, 3, 4, 7], 8, calories: [Cal(1, 1800), Cal(8, 1800)], windowStart: 7);
        Assert.All(a, x => Assert.True(x.Date >= D(7)));
        Assert.Equal(new[] { PointsReason.Workout, PointsReason.CalorieTarget }, a.Select(x => x.Reason));
    }
}
