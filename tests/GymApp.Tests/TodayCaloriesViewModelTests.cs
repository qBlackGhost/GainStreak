using GymApp.App.ViewModels;
using GymApp.Core;
using GymApp.Services;

namespace GymApp.Tests;

/// <summary>The phones' calories card, shared by the Calories screen and Home (Phase 27).</summary>
public class TodayCaloriesViewModelTests
{
    private static readonly DateOnly Day = new(2026, 11, 23);

    private static CalorieOverview Overview(int? goal, int? eaten)
    {
        var info = new CalorieGoalInfo(GoalType.Maintain, goal, null, null, false, Sex.Male);
        var today = eaten is { } e ? new CalorieDay(1, Day, e, goal, GoalType.Maintain, goal is { } g ? CalorieGoals.HitTarget(g, e) : null) : null;
        return new CalorieOverview(info, today, today is null ? [] : [today]);
    }

    [Fact] public void WithoutAGoal_SaysNotSet_AndEatenStartsAtZero()
    {
        var vm = new TodayCaloriesViewModel();
        vm.Apply(Overview(null, null));
        Assert.Equal(("Not set", "", "0"), (vm.GoalStatText, vm.GoalStatUnit, vm.EatenStatText));
        Assert.False(vm.HasGoal);
        Assert.Equal("Not logged yet", vm.StatusText);
        Assert.Equal(0, vm.RingSweep);
    }

    [Fact] public void UnderTheGoal_ShowsWhatIsLeft_AndOverItTurnsTheRingOrange()
    {
        var vm = new TodayCaloriesViewModel();
        vm.Apply(Overview(2504, 1195));
        Assert.Equal(("2,504", " kcal", "1,195"), (vm.GoalStatText, vm.GoalStatUnit, vm.EatenStatText));
        Assert.Equal(("1,309", "kcal left"), (vm.RingCenter, vm.RingCenterSub));
        Assert.Equal("Under target", vm.StatusText);
        Assert.False(vm.IsOver);
        Assert.Equal(360.0 * 1195 / 2504, vm.RingSweep, 3);

        vm.Apply(Overview(2000, 2600));
        Assert.True(vm.IsOver);
        Assert.Equal(("+600", "kcal over goal"), (vm.RingCenter, vm.RingCenterSub));
        Assert.Equal(360, vm.RingSweep);
    }

    [Fact] public void TheBars_FillAgainstTheTargets_AndSayWhetherAnythingIsLogged()
    {
        var vm = new TodayCaloriesViewModel();
        vm.ApplyFood(new DayMacros(Day, 0, 0, 0, 0, false, null), 144, 320, 72);
        Assert.False(vm.HasItems);
        Assert.Equal("0 g", vm.ProteinBar.GramsText);

        var m = new DayMacros(Day, 1195, 90, 130, 35, true, MacroSplit.From(90, 130, 35), EntryCount: 1);
        vm.ApplyFood(m, 144, 320, 72);
        Assert.True(vm.HasItems);
        Assert.Equal(90.0 / 144, vm.ProteinBar.Fill, 6);
        Assert.Equal(("130 g", 130.0 / 320), (vm.CarbsBar.GramsText, vm.CarbsBar.Fill));
        Assert.Equal("90 / 144 g", vm.ProteinBar.Amount);
        Assert.False(vm.ProteinBar.IsOver);
    }

    [Fact] public void AMacroPastItsTarget_IsOver_AndWithoutATargetShowsOnlyGrams()
    {
        var vm = new TodayCaloriesViewModel();
        vm.ApplyFood(new DayMacros(Day, 2900, 138, 380, 70, true, MacroSplit.From(138, 380, 70), EntryCount: 3), 144, 320, null);
        Assert.Equal(("380 / 320 g", true, 1.0), (vm.CarbsBar.Amount, vm.CarbsBar.IsOver, vm.CarbsBar.Fill));
        Assert.False(vm.ProteinBar.IsOver);
        Assert.Equal("70 g", vm.FatBar.Amount);   // no fat target: its share of the day's calories, grams only
    }

    [Fact] public void UnderTheRange_IsOrange_AndOverItIsRed()
    {
        var vm = new TodayCaloriesViewModel();
        vm.Apply(Overview(2504, 1195));
        Assert.Equal((true, false), (vm.IsUnder, vm.IsOver));
        vm.Apply(Overview(2504, 2900));
        Assert.Equal((false, true), (vm.IsUnder, vm.IsOver));
        vm.Apply(Overview(2504, 2600));   // past the goal but inside the range: on target
        Assert.Equal((false, false, true), (vm.IsUnder, vm.IsOver, vm.IsHit));
    }
}
