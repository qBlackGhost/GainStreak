using GymApp.Core;

namespace GymApp.Tests;

public class VacationRulesTests
{
    // 2026-10-05 is a Monday
    private static readonly DateOnly Mon = new(2026, 10, 5);
    private static DateOnly D(int n) => Mon.AddDays(n);

    [Fact] public void FrozenDays_StartUpToTheDayBeforeTheReturn() =>
        Assert.Equal([D(0), D(1), D(2)], VacationRules.FrozenDays(new VacationPeriod(D(0), D(3))));

    [Fact] public void FrozenDays_EndedEarly_StopOnThatDay() =>
        Assert.Equal([D(0), D(1)], VacationRules.FrozenDays(new VacationPeriod(D(0), D(7), D(1))));

    [Fact] public void FrozenDays_OfSeveralVacations_AreMerged() =>
        Assert.Equal(new HashSet<DateOnly> { D(0), D(70) },
            VacationRules.FrozenDays([new VacationPeriod(D(0), D(1)), new VacationPeriod(D(70), D(71))]));

    [Fact] public void ReturnDates_FromTheDayAfterTheStart_ToSevenDaysAfter()
    {
        Assert.Equal((D(1), D(7)), VacationRules.ReturnDateRange(D(0)));
        Assert.True(VacationRules.IsValidReturnDate(D(1), D(0)));
        Assert.True(VacationRules.IsValidReturnDate(D(7), D(0)));
        Assert.False(VacationRules.IsValidReturnDate(D(8), D(0)));
        Assert.False(VacationRules.IsValidReturnDate(D(0), D(0)));
    }

    [Fact] public void StartsTomorrow_WhenTodayIsAlreadyLogged()
    {
        Assert.Equal(D(0), VacationRules.StartDate(D(0), loggedToday: false));
        Assert.Equal(D(1), VacationRules.StartDate(D(0), loggedToday: true));
    }

    [Fact] public void CanStart_WithNoVacationYet() =>
        Assert.True(VacationRules.CanStart([], StreakStatus.PendingToday, D(0)).Allowed);

    [Fact] public void Cooldown_SixtyDaysFromThePreviousStart()
    {
        VacationPeriod[] past = [new(D(0), D(3))];
        Assert.Equal(new VacationCheck(VacationRefusal.Cooldown, D(60)), VacationRules.CanStart(past, StreakStatus.Active, D(59)));
        Assert.True(VacationRules.CanStart(past, StreakStatus.Active, D(60)).Allowed);
    }

    [Fact] public void Cooldown_EndingEarlyStillUsesItUp() =>
        Assert.Equal(VacationRefusal.Cooldown,
            VacationRules.CanStart([new VacationPeriod(D(0), D(7), D(0))], StreakStatus.Active, D(5)).Refusal);

    [Fact] public void AlreadyOnVacation_DuringOne_OrBeforeOneThatStartsTomorrow()
    {
        Assert.Equal(VacationRefusal.AlreadyOnVacation,
            VacationRules.CanStart([new VacationPeriod(D(0), D(3))], StreakStatus.Active, D(2)).Refusal);
        Assert.Equal(VacationRefusal.AlreadyOnVacation,
            VacationRules.CanStart([new VacationPeriod(D(1), D(3))], StreakStatus.Active, D(0)).Refusal);
    }

    [Theory]
    [InlineData(StreakStatus.NeedsChoice, VacationRefusal.SettleMissedDay)]
    [InlineData(StreakStatus.AtRisk, VacationRefusal.SettleMissedDay)]
    [InlineData(StreakStatus.Shifted, VacationRefusal.None)]
    [InlineData(StreakStatus.PendingToday, VacationRefusal.None)]
    [InlineData(StreakStatus.Active, VacationRefusal.None)]
    [InlineData(StreakStatus.Broken, VacationRefusal.None)]
    public void AMissedDayIsSettledFirst(StreakStatus status, VacationRefusal expected) =>
        Assert.Equal(expected, VacationRules.CanStart([], status, D(0)).Refusal);

    [Fact] public void Current_IsTheVacationWithAFrozenDayTodayOrLater()
    {
        VacationPeriod[] all = [new(D(0), D(3)), new(D(70), D(72))];
        Assert.Equal(all[0], VacationRules.Current(all, D(2)));
        Assert.Null(VacationRules.Current(all, D(3)));                 // the return date is a normal day
        Assert.Equal(all[1], VacationRules.Current(all, D(69)));       // starts tomorrow
    }

    // A box roughly the size of Bucharest. 0.1° of latitude is about 11 km.
    private const double S = 44.33, W = 25.97, N = 44.54, E = 26.23;
    private static bool Away(double lat, double lon) => VacationRules.IsAway(new LocationFix(lat, lon, 50), S, W, N, E);

    [Fact] public void IsAway_InsideTheCity_No() => Assert.False(Away(44.43, 26.10));

    [Fact] public void IsAway_JustOutside_No() => Assert.False(Away(N + 0.3, 26.10));      // ~33 km north of the edge

    [Fact] public void IsAway_BeyondFortyKilometres_Yes()
    {
        Assert.True(Away(N + 0.4, 26.10));                                                  // ~44 km north
        Assert.True(Away(41.90, 12.50));                                                    // Rome
    }

    [Fact] public void DistanceToBox_IsZeroInside_AndMeasuredToTheNearestEdge()
    {
        Assert.Equal(0, VacationRules.DistanceToBoxMeters(44.43, 26.10, S, W, N, E));
        Assert.Equal(GymProximity.DistanceMeters(N + 0.3, 26.10, N, 26.10),
            VacationRules.DistanceToBoxMeters(N + 0.3, 26.10, S, W, N, E), 3);
    }
}
