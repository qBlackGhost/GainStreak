using GymApp.Core;
using GymApp.Services;

namespace GymApp.Tests;

/// <summary>Which reminders the phones schedule (16g). The week is Mon Tue Thu Fri training, Wed Sat Sun rest; Monday
/// 26 October 2026 is the week under test.</summary>
public sealed class ReminderPlannerTests
{
    private const string Week = "WWRWWRR";
    private static readonly DateOnly Mon = new(2026, 10, 26), Tue = Mon.AddDays(1), Wed = Mon.AddDays(2),
        Thu = Mon.AddDays(3), Fri = Mon.AddDays(4), Sun = Mon.AddDays(6);
    private static readonly ScheduleChange[] Schedule = [new(new DateOnly(2026, 1, 1), Week)];
    private static readonly ReminderSettings On = new(true, true, new TimeOnly(18, 0), true);
    private static readonly Dictionary<DateOnly, MissChoice> NoChoices = [];

    /// <summary>The week before (Mon to Fri, rest days counting), so there's a streak at stake.</summary>
    private static readonly DateOnly[] LastWeek =
        [Mon.AddDays(-7), Mon.AddDays(-6), Mon.AddDays(-4), Mon.AddDays(-3)];

    private static string? Name(DateOnly d) => d.DayOfWeek.ToString()[..3] + " workout";

    private static IReadOnlyList<PlannedReminder> Plan(IEnumerable<DateOnly> dates, DateTime now,
        IReadOnlyDictionary<DateOnly, MissChoice>? choices = null, ReminderSettings? settings = null,
        IReadOnlyList<ScheduleChange>? schedule = null) =>
        ReminderPlanner.Plan(settings ?? On, dates, schedule ?? Schedule, choices ?? NoChoices, now, Name);

    private static DateTime At(DateOnly d, int hour) => d.ToDateTime(new TimeOnly(hour, 0));

    private static PlannedReminder? Find(IReadOnlyList<PlannedReminder> plan, ReminderKind kind, DateOnly day) =>
        plan.SingleOrDefault(r => r.Kind == kind && r.Date == day);

    [Fact] public void ATrainingDay_WithNoCheckIn_GetsItsReminder_AndTheMissedOneTheNextMorning()
    {
        var plan = Plan(LastWeek, At(Mon, 8));

        var today = Find(plan, ReminderKind.TrainingDay, Mon)!;
        Assert.Equal(new TimeOnly(18, 0), today.Time);
        Assert.Equal("Today is Mon workout", today.Title);
        Assert.Contains("keep your 7-day streak", today.Body);   // last week: four workouts and three rest days
        var missed = Find(plan, ReminderKind.MissedDay, Tue)!;
        Assert.Equal(new TimeOnly(9, 0), missed.Time);
        Assert.Contains("You missed Mon workout yesterday", missed.Body);
    }

    [Fact] public void ACheckIn_DropsTodaysReminders()
    {
        var plan = Plan([.. LastWeek, Mon], At(Mon, 12));

        Assert.Null(Find(plan, ReminderKind.TrainingDay, Mon));
        Assert.Null(Find(plan, ReminderKind.MissedDay, Tue));
        Assert.NotNull(Find(plan, ReminderKind.TrainingDay, Tue));   // tomorrow's stays
    }

    [Fact] public void TodaysReminder_IsntPlannedOnceItsTimeHasPassed()
    {
        var plan = Plan(LastWeek, At(Mon, 19));

        Assert.Null(Find(plan, ReminderKind.TrainingDay, Mon));
        Assert.NotNull(Find(plan, ReminderKind.MissedDay, Tue));   // the miss can still happen
    }

    [Fact] public void ARestDay_GetsNone()
    {
        var plan = Plan([.. LastWeek, Mon, Tue], At(Wed, 8));

        Assert.Null(Find(plan, ReminderKind.TrainingDay, Wed));
        Assert.Null(Find(plan, ReminderKind.MissedDay, Thu));
        Assert.NotNull(Find(plan, ReminderKind.TrainingDay, Thu));
    }

    [Fact] public void TheMorningAfterAMiss_RemindsToChoose()
    {
        var plan = Plan(LastWeek, At(Tue, 7));   // Monday was missed, nothing chosen yet

        var missed = Find(plan, ReminderKind.MissedDay, Tue)!;
        Assert.Contains("You missed Mon workout yesterday", missed.Body);
        Assert.NotNull(Find(plan, ReminderKind.TrainingDay, Tue));   // today's workout is still owed
    }

    [Fact] public void AMissAlreadyResolved_HasNoMorningReminder()
    {
        var choices = new Dictionary<DateOnly, MissChoice> { [Mon] = MissChoice.ShiftToRest };
        var plan = Plan(LastWeek, At(Tue, 7), choices);

        Assert.Null(Find(plan, ReminderKind.MissedDay, Tue));
        Assert.Equal("Today is Mon workout", Find(plan, ReminderKind.TrainingDay, Tue)!.Title);   // the made-up day's name
    }

    [Fact] public void AMadeUpDay_OwesTomorrow_EvenOnARestDay()
    {
        // Thursday missed, made up on Friday (ShiftToRest): Friday's own workout is owed on Saturday, a rest day
        var choices = new Dictionary<DateOnly, MissChoice> { [Thu] = MissChoice.ShiftToRest };
        var plan = Plan([.. LastWeek, Mon, Tue, Fri], At(Fri, 20), choices);

        Assert.NotNull(Find(plan, ReminderKind.TrainingDay, Fri.AddDays(1)));
    }

    [Fact] public void TheWeekShift_StillRemindsToday()
    {
        var choices = new Dictionary<DateOnly, MissChoice> { [Mon] = MissChoice.ShiftWeek };
        var plan = Plan(LastWeek, At(Tue, 10), choices);

        Assert.Null(Find(plan, ReminderKind.MissedDay, Tue));
        Assert.NotNull(Find(plan, ReminderKind.TrainingDay, Tue));
    }

    [Fact] public void Off_PlansNothing_AndEachSwitchHoldsItsOwn()
    {
        Assert.Empty(Plan(LastWeek, At(Mon, 8), settings: ReminderSettings.Off));

        var trainingOnly = Plan(LastWeek, At(Mon, 8), settings: On with { MissedDay = false });
        Assert.All(trainingOnly, r => Assert.Equal(ReminderKind.TrainingDay, r.Kind));
        var missedOnly = Plan(LastWeek, At(Mon, 8), settings: On with { TrainingDay = false });
        Assert.All(missedOnly, r => Assert.Equal(ReminderKind.MissedDay, r.Kind));
        Assert.NotEmpty(missedOnly);
    }

    [Fact] public void NoStreakAtStake_HasNoMissedReminders()
    {
        var plan = Plan([], At(Mon, 8));   // never trained: nothing to lose

        Assert.DoesNotContain(plan, r => r.Kind == ReminderKind.MissedDay && r.Date == Tue);
        Assert.Contains("start your streak", Find(plan, ReminderKind.TrainingDay, Mon)!.Body);
    }

    [Fact] public void AScheduleChangeMidWeek_UsesTheNewWeekFromItsDay()
    {
        ScheduleChange[] schedule = [Schedule[0], new(Thu, "RRRRRRW")];   // from Thursday: Sundays only
        var plan = Plan([.. LastWeek, Mon], At(Tue, 8), schedule: schedule);

        Assert.NotNull(Find(plan, ReminderKind.TrainingDay, Tue));
        Assert.Null(Find(plan, ReminderKind.TrainingDay, Thu));
        Assert.Null(Find(plan, ReminderKind.TrainingDay, Fri));
        Assert.NotNull(Find(plan, ReminderKind.TrainingDay, Sun));
    }

    [Fact] public void TheWeekAhead_IsCovered_AndNothingBeyond()
    {
        var plan = Plan(LastWeek, At(Mon, 8));

        Assert.All(plan, r => Assert.InRange(r.Date, Mon, Mon.AddDays(ReminderPlanner.Days - 1)));
        Assert.Equal([Mon, Tue, Thu, Fri], plan.Where(r => r.Kind == ReminderKind.TrainingDay).Select(r => r.Date));
        Assert.Equal(plan.Count, plan.Select(r => r.Id).Distinct().Count());
    }

    [Fact] public void Reminders_KeepTheirClockTime_AcrossDaylightSaving()
    {
        // Romania leaves summer time on Sunday 25 October 2026: 18:00 is 15:00 UTC on Saturday, 16:00 UTC on Monday
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bucharest");
        var sat = Mon.AddDays(-2);
        ScheduleChange[] everyDay = [new(new DateOnly(2026, 1, 1), "WWWWWWW")];
        var plan = Plan([sat.AddDays(-1)], At(sat, 8), schedule: everyDay);

        var before = Find(plan, ReminderKind.TrainingDay, sat)!;
        var after = Find(plan, ReminderKind.TrainingDay, Mon)!;
        Assert.Equal(new TimeOnly(18, 0), before.Time);
        Assert.Equal(new TimeOnly(18, 0), after.Time);
        Assert.Equal(new DateTime(2026, 10, 24, 15, 0, 0), before.At(zone).UtcDateTime);
        Assert.Equal(new DateTime(2026, 10, 26, 16, 0, 0), after.At(zone).UtcDateTime);
    }
}
