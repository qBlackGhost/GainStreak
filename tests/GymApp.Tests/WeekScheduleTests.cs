using GymApp.Core;

namespace GymApp.Tests;

public class WeekScheduleTests
{
    // Monday-first; "-" = rest, number = index in the split's rotation
    private static string Describe(IReadOnlyList<ScheduleSlot> slots) =>
        string.Join(" ", slots.Select(s => s.RotationIndex?.ToString() ?? "-"));

    [Fact] public void Ppl_StartingMonday() =>
        Assert.Equal("0 1 2 - 0 1 2", Describe(WeekSchedule.Build("WWWRWWW", DayOfWeek.Monday, 3)));

    [Fact] public void UpperLower_StartingMonday() =>
        Assert.Equal("0 1 - 0 1 - -", Describe(WeekSchedule.Build("WWRWWRR", DayOfWeek.Monday, 2)));

    [Fact] public void BroSplit_StartingWednesday_WrapsAroundTheWeek() =>
        Assert.Equal("- - 0 1 2 3 4", Describe(WeekSchedule.Build("WWWWWRR", DayOfWeek.Wednesday, 5)));

    [Fact] public void FullBody2_StartingMonday_TrainsMondayAndThursday()
    {
        var slots = WeekSchedule.Build("WRRWRRR", DayOfWeek.Monday, 1);
        Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Thursday }, WeekSchedule.TrainingWeekdays(slots));
    }

    [Fact] public void FullBody3_StartingFriday_TrainsFridaySundayTuesday()
    {
        var slots = WeekSchedule.Build("WRWRWRR", DayOfWeek.Friday, 1);
        Assert.Equal(new[] { DayOfWeek.Tuesday, DayOfWeek.Friday, DayOfWeek.Sunday },
                     WeekSchedule.TrainingWeekdays(slots));
    }

    [Fact] public void Build_IsAlwaysMondayFirst()
    {
        var slots = WeekSchedule.Build("WWWRWWW", DayOfWeek.Saturday, 3);
        Assert.Equal(DayOfWeek.Monday, slots[0].Weekday);
        Assert.Equal(DayOfWeek.Sunday, slots[6].Weekday);
    }

    [Theory]
    [InlineData("WWW")]
    [InlineData("WWWWWWWW")]
    [InlineData("RRRRRRR")]
    [InlineData("wwwrwww")]
    [InlineData("WWWRWWX")]
    public void Build_RejectsInvalidPatterns(string pattern) =>
        Assert.Throws<ArgumentException>(() => WeekSchedule.Build(pattern, DayOfWeek.Monday, 3));

    [Fact] public void FromWeekdays_NumbersTrainingDaysInOrder() =>
        Assert.Equal("0 - 1 - 2 - -",
            Describe(WeekSchedule.FromWeekdays(new[] { DayOfWeek.Friday, DayOfWeek.Monday, DayOfWeek.Wednesday })));
}
