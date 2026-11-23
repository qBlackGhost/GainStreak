using CommunityToolkit.Mvvm.Input;
using GymApp.Core;
using GymApp.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace GymApp.App.ViewModels;

/// <summary>One legend row under the past-day ring: a colour dot, the name, the grams and the share.</summary>
public record MacroLegendRowViewModel(Macro Macro, string Name, string GramsText, string PercentText)
{
    public bool IsProtein => Macro == Macro.Protein;
    public bool IsCarbs => Macro == Macro.Carbs;
    public bool IsFat => Macro == Macro.Fat;
}

/// <summary>
/// A past day from the 7-day chart (Phase 27d): a centred card on a dimmed page with the day, a status line, the macro ring and
/// its legend, and "See what you ate". Three states: entries (ring split and legend), a total only (the kcal, no split), and
/// nothing logged (no ring). Read-only, like every past day.
/// </summary>
public partial class PastDayPopupViewModel : ViewModelBase
{
    private readonly Action _seeFood;
    private readonly Action _close;

    public PastDayPopupViewModel(DateOnly date, CalorieDay? day, DayMacros macros, Action seeFood, Action close)
    {
        _seeFood = seeFood;
        _close = close;
        Date = date;
        Title = date.ToString("ddd d MMM", CultureInfo.InvariantCulture);

        HasEntries = macros.HasEntries;
        IsLogged = day is not null || macros.HasEntries;
        IsTotalOnly = IsLogged && !HasEntries;
        Split = HasEntries ? macros.Split : null;
        EatenKcal = day?.Consumed ?? macros.Kcal;
        GoalKcal = day?.Goal;
        TotalText = $"{CalorieGoals.K(EatenKcal)} kcal";

        if (!IsLogged) StatusText = "Not logged";
        else if (GoalKcal is { } goal)
        {
            var (lo, hi) = CalorieGoals.TargetRange(goal);
            IsHit = EatenKcal >= lo && EatenKcal <= hi;
            IsOver = EatenKcal > hi;
            IsUnder = EatenKcal < lo;
            StatusText = IsHit ? "On target"
                       : $"{(IsOver ? "Over" : "Under")} target · goal {CalorieGoals.K(goal)}";
        }
        else StatusText = "Logged · no goal that day";

        Legend = Split is { } s
            ?
            [
                Row(Macro.Protein, "Protein", s.ProteinGrams, s),
                Row(Macro.Carbs, "Carbs", s.CarbsGrams, s),
                Row(Macro.Fat, "Fat", s.FatGrams, s),
            ]
            : [];
    }

    /// <summary>Reads the day's log and food, then builds the popup.</summary>
    public static async Task<PastDayPopupViewModel> LoadAsync(CalorieService calories, FoodService food, int userId, DateOnly date,
        Action seeFood, Action close)
    {
        var day = await calories.GetDayAsync(userId, date);
        var macros = await food.GetDayMacrosAsync(userId, date);
        return new PastDayPopupViewModel(date, day, macros, seeFood, close);
    }

    public DateOnly Date { get; }
    /// <summary>"Wed 18 Nov".</summary>
    public string Title { get; }
    /// <summary>"Over target · goal 1,900", "On target", "Not logged".</summary>
    public string StatusText { get; }
    public bool IsHit { get; }
    public bool IsOver { get; }
    public bool IsUnder { get; }
    public bool IsOff => IsOver || IsUnder;

    public bool IsLogged { get; }
    public bool IsNotLogged => !IsLogged;
    public bool HasEntries { get; }
    /// <summary>Logged as one number, with no foods: the kcal and no split.</summary>
    public bool IsTotalOnly { get; }
    /// <summary>The ring shows for any logged day; only a day with entries has a split.</summary>
    public bool ShowRing => IsLogged;

    public MacroSplit? Split { get; }
    public int EatenKcal { get; }
    public int? GoalKcal { get; }
    public string TotalText { get; }
    public IReadOnlyList<MacroLegendRowViewModel> Legend { get; }

    [RelayCommand] private void SeeFood() { _close(); _seeFood(); }
    [RelayCommand] private void Close() => _close();

    private static MacroLegendRowViewModel Row(Macro macro, string name, double grams, MacroSplit split) =>
        new(macro, name, Math.Round(grams, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + " g",
            split.PercentOf(macro).ToString(CultureInfo.InvariantCulture) + "%");
}
