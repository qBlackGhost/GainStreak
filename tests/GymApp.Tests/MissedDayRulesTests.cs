using GymApp.Core;

namespace GymApp.Tests;

public class MissedDayRulesTests
{
    // 2026-10-05 is a Monday
    private static readonly DateOnly Mon = new(2026, 10, 5);
    private static DateOnly D(int n) => Mon.AddDays(n);

    // PPL from Monday: Mon Push, Tue Pull, Wed Legs, Thu Rest, Fri Push, Sat Pull, Sun Legs
    private static readonly IReadOnlyList<ScheduleSlot> Ppl = WeekSchedule.Build("WWWRWWW", DayOfWeek.Monday, 3);
    private static readonly string[] Names = ["Push", "Pull", "Legs"];

    [Fact] public void Limits_CountByTheWeekOfTheMissedDay()
    {
        // a shift for Sunday's miss doesn't count against the next Monday's
        var choices = new Dictionary<DateOnly, MissChoice> { [D(6)] = MissChoice.ShiftWeek };
        Assert.Equal(0, MissedDayRules.Left(MissChoice.ShiftWeek, D(6), choices));
        Assert.Equal(1, MissedDayRules.Left(MissChoice.ShiftWeek, D(7), choices));
    }

    [Fact] public void Limits_AreSeparatePerOption()
    {
        var choices = new Dictionary<DateOnly, MissChoice> { [D(1)] = MissChoice.ShiftWeek, [D(2)] = MissChoice.ShiftToRest };
        Assert.Equal(0, MissedDayRules.Left(MissChoice.ShiftWeek, D(4), choices));
        Assert.Equal(1, MissedDayRules.Left(MissChoice.ShiftToRest, D(4), choices));
        Assert.Equal(int.MaxValue, MissedDayRules.Left(MissChoice.Continue, D(4), choices));
    }

    [Fact] public void ShiftedWeek_MatchesMovingThePresetStartDay() =>
        Assert.Equal(WeekSchedule.Build("WWWRWWW", DayOfWeek.Tuesday, 3), MissedDayRules.ShiftedWeek(Ppl));

    [Fact] public void MakeUpText_WhenTodayIsTheRestDay() =>
        // Wed (Legs) missed, today Thu is the rest day that absorbs it
        Assert.Equal("Legs today instead of resting, back to normal on Friday.", MissedDayRules.MakeUpText(Ppl, Names, D(3)));

    [Fact] public void MakeUpText_AcrossSeveralTrainingDays() =>
        // Fri (Push) missed, today Sat: the chain runs to next Thu
        Assert.Equal("Push today, Pull on Sunday, Legs on Monday, Push on Tuesday, Pull on Wednesday, " +
                     "Legs on Thursday instead of resting, back to normal on Friday.",
            MissedDayRules.MakeUpText(Ppl, Names, D(5)));

    [Fact] public void ShiftWeekText_SeveralRestDays()
    {
        var upperLower = WeekSchedule.Build("WWRWWRR", DayOfWeek.Monday, 2);
        Assert.Equal("Lower today, and every day after moves one day later. " +
                     "Your rest days move from Wednesday, Saturday and Sunday to Monday, Thursday and Sunday.",
            MissedDayRules.ShiftWeekText(upperLower, ["Upper", "Lower"], D(1)));
    }

    [Fact] public void ContinueText_OnARestDay() =>
        Assert.Equal("Today stays a rest day. Your streak resets to 0.", MissedDayRules.ContinueText(Ppl, Names, D(3)));
}
