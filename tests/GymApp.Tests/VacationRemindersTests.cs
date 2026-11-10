using GymApp.Core;
using GymApp.Services;

namespace GymApp.Tests;

/// <summary>Reminders around a vacation (0.19.0). The week is Mon Tue Thu Fri training, Wed Sat Sun rest (Monday
/// 26 October 2026); away Tue to Thu, back on Friday.</summary>
public sealed class VacationRemindersTests
{
    private static readonly DateOnly Mon = new(2026, 10, 26), Tue = Mon.AddDays(1), Thu = Mon.AddDays(3), Fri = Mon.AddDays(4);
    private static readonly ScheduleChange[] Schedule = [new(new DateOnly(2026, 1, 1), "WWRWWRR")];
    private static readonly ReminderSettings On = new(true, true, new TimeOnly(18, 0), true);
    private static readonly DateOnly[] LastWeek = [Mon.AddDays(-7), Mon.AddDays(-6), Mon.AddDays(-4), Mon.AddDays(-3)];

    private static RestDayRule Away(DateOnly from, DateOnly to)
    {
        var frozen = new HashSet<DateOnly>();
        for (var d = from; d <= to; d = d.AddDays(1)) frozen.Add(d);
        return new RestDayRule(new DateOnly(2026, 1, 1), new HashSet<DateOnly>(), frozen);
    }

    private static IReadOnlyList<PlannedReminder> Plan(DateTime now, RestDayRule rule) =>
        ReminderPlanner.Plan(On, LastWeek, Schedule, new Dictionary<DateOnly, MissChoice>(), now, _ => null, rule);

    private static DateTime At(DateOnly d, int hour) => d.ToDateTime(new TimeOnly(hour, 0));

    [Fact]
    public void NothingIsPlannedOnVacationDays()
    {
        var plan = Plan(At(Mon, 8), Away(Tue, Thu));
        Assert.DoesNotContain(plan, r => r.Date >= Tue && r.Date <= Thu && r.Kind != ReminderKind.WelcomeBack);
        Assert.Contains(plan, r => r.Kind == ReminderKind.TrainingDay && r.Date == Mon);
        Assert.Contains(plan, r => r.Kind == ReminderKind.TrainingDay && r.Date == Fri);
    }

    [Fact]
    public void NoMissedDayReminder_OnTheFirstVacationMorning() =>
        // a miss on Monday is closed by Tuesday's vacation: nothing to choose
        Assert.DoesNotContain(Plan(At(Mon, 8), Away(Tue, Thu)), r => r.Kind == ReminderKind.MissedDay && r.Date == Tue);

    [Fact]
    public void WelcomeBack_At9_OnTheFirstNormalDay()
    {
        var w = Assert.Single(Plan(At(Mon, 8), Away(Tue, Thu)), r => r.Kind == ReminderKind.WelcomeBack);
        Assert.Equal(Fri, w.Date);
        Assert.Equal(new TimeOnly(9, 0), w.Time);
        Assert.Equal("Welcome back!", w.Title);
    }

    [Fact]
    public void WelcomeBack_NotWhenItsTimeHasPassed()
    {
        Assert.Contains(Plan(At(Fri, 8), Away(Tue, Thu)), r => r.Kind == ReminderKind.WelcomeBack);
        Assert.DoesNotContain(Plan(At(Fri, 10), Away(Tue, Thu)), r => r.Kind == ReminderKind.WelcomeBack);
    }

    [Fact]
    public void KindsHaveTheirOwnIds()
    {
        var plan = Plan(At(Mon, 8), Away(Tue, Thu));
        Assert.Equal(plan.Count, plan.Select(r => r.Id).Distinct().Count());
        Assert.InRange(plan.Single(r => r.Kind == ReminderKind.WelcomeBack).Id, 3_000_000, 3_999_999);
    }
}
