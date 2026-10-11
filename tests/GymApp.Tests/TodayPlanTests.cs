using GymApp.Core;

namespace GymApp.Tests;

public class TodayPlanTests
{
    // 2026-10-05 is a Monday: Day(5) = Mon ... Day(11) = Sun
    private static DateOnly Day(int d) => new(2026, 10, d);
    private static readonly string[] Ppl = ["Push", "Pull", "Legs"];

    private static string Title(IReadOnlyList<ScheduleSlot> week, string[] names, int day) =>
        TodayPlan.Resolve(week, names, Day(day)).Title;

    [Fact] public void Ppl_StartingMonday_FollowsTheRotation()
    {
        var week = WeekSchedule.Build("WWWRWWW", DayOfWeek.Monday, 3);
        Assert.Equal("Push", Title(week, Ppl, 5));        // Mon
        Assert.Equal("Legs", Title(week, Ppl, 7));        // Wed
        Assert.Equal("Rest day", Title(week, Ppl, 8));    // Thu
        Assert.Equal("Push", Title(week, Ppl, 9));        // Fri: the rotation restarts after the rest day
        Assert.Equal("Legs", Title(week, Ppl, 11));       // Sun
    }

    [Fact] public void Ppl_StartingWednesday_ShiftsTheWholeWeek()
    {
        var week = WeekSchedule.Build("WWWRWWW", DayOfWeek.Wednesday, 3);
        Assert.Equal("Push", Title(week, Ppl, 7));        // Wed is day 1
        Assert.Equal("Rest day", Title(week, Ppl, 10));   // Sat
        Assert.Equal("Push", Title(week, Ppl, 11));       // Sun
        Assert.Equal("Pull", Title(week, Ppl, 5));        // Mon
    }

    [Fact] public void CustomSplit_UsesTheNthTrainingDay()
    {
        var week = WeekSchedule.FromWeekdays(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday });
        string[] names = ["Chest", "Back", "Legs"];
        Assert.Equal("Back", Title(week, names, 7));      // Wed
        Assert.Equal("Rest day", Title(week, names, 6));  // Tue
    }

    [Fact] public void FullBody_EveryTrainingDayIsFullBody()
    {
        var week = WeekSchedule.Build("WRRWRRR", DayOfWeek.Monday, 1);
        Assert.Equal("Full Body", Title(week, ["Full Body"], 8));   // Thu
    }

    [Fact] public void RestDay_HasNoRotationIndex()
    {
        var week = WeekSchedule.Build("WWWRWWW", DayOfWeek.Monday, 3);
        var rest = TodayPlan.Resolve(week, Ppl, Day(8));
        Assert.False(rest.IsTraining);
        Assert.Null(rest.RotationIndex);

        var push = TodayPlan.Resolve(week, Ppl, Day(5));
        Assert.True(push.IsTraining);
        Assert.Equal(0, push.RotationIndex);
    }

    [Fact] public void NoDayNames_FallsBackToWorkout()
    {
        var week = WeekSchedule.Build("WWWRWWW", DayOfWeek.Monday, 3);
        Assert.Equal("Workout", Title(week, [], 5));
    }
}
