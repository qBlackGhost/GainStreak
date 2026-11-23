using CommunityToolkit.Mvvm.ComponentModel;
using GymApp.Core;
using GymApp.Services;
using System;
using System.Globalization;

namespace GymApp.App.ViewModels;

/// <summary>
/// Today's calories as the phones show them (Phase 27): the status, the lime ring with what is left, Goal and Eaten, the target
/// range and the three macro bars. One model for the Calories screen's card and Home's, drawn by <c>Views/TodayCaloriesCard</c>.
/// </summary>
public partial class TodayCaloriesViewModel : ObservableObject
{
    [ObservableProperty] public partial string StatusText { get; set; } = "Not logged yet";
    [ObservableProperty] public partial bool IsHit { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnder))]
    public partial bool IsOff { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnder))]
    public partial bool IsOver { get; set; }
    /// <summary>Logged but under the range: the pill is orange; over it, everything over is red.</summary>
    public bool IsUnder => IsOff && !IsOver;
    [ObservableProperty] public partial bool HasGoal { get; set; }
    [ObservableProperty] public partial double RingSweep { get; set; }
    [ObservableProperty] public partial string RingCenter { get; set; } = "—";
    [ObservableProperty] public partial string RingCenterSub { get; set; } = "no goal yet";
    [ObservableProperty] public partial string GoalStatText { get; set; } = "Not set";
    [ObservableProperty] public partial string GoalStatUnit { get; set; } = "";
    [ObservableProperty] public partial string EatenStatText { get; set; } = "0";
    [ObservableProperty] public partial string RangeText { get; set; } = "";
    [ObservableProperty] public partial MacroBarViewModel ProteinBar { get; set; } = new("Protein", "0 g", 0, false);
    [ObservableProperty] public partial MacroBarViewModel CarbsBar { get; set; } = new("Carbs", "0 g", 0, false);
    [ObservableProperty] public partial MacroBarViewModel FatBar { get; set; } = new("Fat", "0 g", 0, false);
    [ObservableProperty] public partial bool HasItems { get; set; }

    private static string K(int kcal) => CalorieGoals.K(kcal);

    /// <summary>Today's log against the goal: the goal a logged day was logged against, else the current one.</summary>
    public void Apply(CalorieOverview o)
    {
        var t = o.Today;
        var goal = t?.Goal ?? o.Goal.Goal;
        HasGoal = goal is not null;
        GoalStatText = goal is { } gs ? K(gs) : "Not set";
        GoalStatUnit = goal is null ? "" : " kcal";
        EatenStatText = K(t?.Consumed ?? 0);

        if (goal is { } g)
        {
            var (lo, hi) = CalorieGoals.TargetRange(g);
            RangeText = $"{K(lo)}–{K(hi)}";
            IsHit = t is { Hit: true };
            IsOver = t is not null && t.Consumed > hi;
            IsOff = t is not null && !IsHit;
            RingSweep = t is null ? 0 : 360 * Math.Min(1, (double)t.Consumed / g);
            var left = g - (t?.Consumed ?? 0);
            RingCenter = t is null ? K(g) : left >= 0 ? K(left) : "+" + K(-left);
            RingCenterSub = t is null ? "kcal to eat today" : left >= 0 ? "kcal left" : "kcal over goal";
            StatusText = t is null ? "Not logged yet"
                       : IsHit ? $"On target · +{PointsCalculator.CalorieTargetPoints} pts"
                       : t.Consumed < lo ? "Under target" : "Over target";
        }
        else
        {
            RangeText = "";
            IsHit = IsOver = IsOff = false;
            RingSweep = 0;
            RingCenter = t is null ? "—" : K(t.Consumed);
            RingCenterSub = t is null ? "no goal yet" : "kcal eaten";
            StatusText = t is null ? "Not logged yet" : "Logged";
        }
    }

    /// <summary>Today's macros: each bar fills grams ÷ target with a target, else its share of the day's calories.</summary>
    public void ApplyFood(DayMacros m, int? proteinTarget, int? carbsTarget, int? fatTarget)
    {
        HasItems = m.EntryCount > 0;
        var split = m.Split;
        MacroBarViewModel Bar(string name, Macro macro, double grams, int? target)
        {
            var fill = MacroSplit.BarFill(grams, target, split?.ShareOf(macro) ?? 0, out var over);
            var amount = target is > 0 and var t ? $"{Whole(grams)} / {t} g" : "";
            return new MacroBarViewModel(name, Grams(grams), fill, over, amount);
        }
        ProteinBar = Bar("Protein", Macro.Protein, m.Protein, proteinTarget);
        CarbsBar = Bar("Carbs", Macro.Carbs, m.Carbs, carbsTarget);
        FatBar = Bar("Fat", Macro.Fat, m.Fat, fatTarget);
    }

    private static string Whole(double g) => Math.Round(g, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
    private static string Grams(double g) => Whole(g) + " g";
}
