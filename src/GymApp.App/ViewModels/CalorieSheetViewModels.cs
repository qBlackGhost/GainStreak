using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Core;
using GymApp.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace GymApp.App.ViewModels;

/// <summary>One choice in a sheet's segmented pill.</summary>
public partial class PillOptionViewModel(string name, Action pick) : ObservableObject
{
    public string Name { get; } = name;
    [ObservableProperty] public partial bool IsSelected { get; set; }
    [RelayCommand] private void Pick() => pick();
}

/// <summary>A recommended macro on the goal sheet: grams and the grams per kg they came from.</summary>
public record MacroTileViewModel(Macro Macro, string Name, string GramsText, string RateText)
{
    public bool IsProtein => Macro == Macro.Protein;
    public bool IsCarbs => Macro == Macro.Carbs;
    public bool IsFat => Macro == Macro.Fat;
}

/// <summary>
/// The Calorie goal sheet (Phase 27, a sheet from the bottom on phones). Lose / Maintain / Gain, then Recommended or Custom.
/// Recommended shows the macros <see cref="MacroPlan"/> sets in grams per kg and the calorie goal they add up to; Custom takes the
/// user's own grams (all three) and works the goal out from them, never typed. Nothing is saved until Use or Save.
/// </summary>
public partial class CalorieGoalSheetViewModel : ViewModelBase
{
    private readonly CalorieService _calories;
    private readonly int _userId;
    private readonly MacroInputs? _inputs;
    private readonly Func<Task> _saved;
    private readonly Action _close;
    private readonly Action _addBodyData;

    public CalorieGoalSheetViewModel(CalorieGoalInfo info, CalorieService calories, int userId, Func<Task> saved, Action close,
        Action addBodyData)
    {
        _calories = calories;
        _userId = userId;
        _inputs = info.MacroInputs;
        _saved = saved;
        _close = close;
        _addBodyData = addBodyData;
        NoRecommendationText = info.SuggestionReason ?? "Add your body data to get a recommendation.";

        GoalTypes = Enum.GetValues<GoalType>().Select(t => new PillOptionViewModel(CalorieGoals.ShortName(t), () => GoalType = t)).ToList();
        Modes = [new("Recommended", () => IsCustom = false), new("Custom", () => IsCustom = true)];

        if (info.MacroMode == MacroMode.Custom)
        {
            ProteinInput = Text(info.ProteinGoalGrams);
            CarbsInput = Text(info.CarbsGoalGrams);
            FatInput = Text(info.FatGoalGrams);
        }
        GoalType = info.GoalType;
        IsCustom = info.MacroMode == MacroMode.Custom || (info.MacroMode is null && _inputs is null);
        Refresh();
    }

    public IReadOnlyList<PillOptionViewModel> GoalTypes { get; }
    public IReadOnlyList<PillOptionViewModel> Modes { get; }

    [ObservableProperty] public partial GoalType GoalType { get; set; }
    [ObservableProperty] public partial int GoalTypeIndex { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecommended), nameof(ShowRecommendation), nameof(ShowNoRecommendation), nameof(SaveLabel))]
    public partial bool IsCustom { get; set; }
    [ObservableProperty] public partial int ModeIndex { get; set; }

    partial void OnGoalTypeChanged(GoalType value) => Refresh();
    partial void OnIsCustomChanged(bool value) => Refresh();

    public bool IsRecommended => !IsCustom;
    public bool HasRecommendation => _inputs is not null;
    public bool ShowRecommendation => IsRecommended && HasRecommendation;
    public bool ShowNoRecommendation => IsRecommended && !HasRecommendation;
    public string NoRecommendationText { get; }
    public string SaveLabel => IsCustom ? "Save" : "Use these targets";

    // ----- Recommended -----
    [ObservableProperty] public partial IReadOnlyList<MacroTileViewModel> Tiles { get; set; } = [];
    [ObservableProperty] public partial string RecommendedKcalText { get; set; } = "";
    [ObservableProperty] public partial string RecommendedNote { get; set; } = "";

    // ----- Custom -----
    [ObservableProperty] public partial string ProteinInput { get; set; } = "";
    [ObservableProperty] public partial string CarbsInput { get; set; } = "";
    [ObservableProperty] public partial string FatInput { get; set; } = "";
    [ObservableProperty] public partial string CustomKcalText { get; set; } = "—";
    [ObservableProperty] public partial string CustomNote { get; set; } = "Enter all three to see your calorie goal.";

    partial void OnProteinInputChanged(string value) => UpdateCustom();
    partial void OnCarbsInputChanged(string value) => UpdateCustom();
    partial void OnFatInputChanged(string value) => UpdateCustom();

    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial bool IsSaving { get; set; }

    /// <summary>The recommendation for the selected goal type, or null without body data.</summary>
    public MacroRecommendation? Recommendation => _inputs?.For(GoalType);

    private void Refresh()
    {
        Error = null;
        GoalTypeIndex = (int)GoalType;
        ModeIndex = IsCustom ? 1 : 0;
        for (var i = 0; i < GoalTypes.Count; i++) GoalTypes[i].IsSelected = i == GoalTypeIndex;
        for (var i = 0; i < Modes.Count; i++) Modes[i].IsSelected = i == ModeIndex;

        if (Recommendation is { } r && _inputs is { } inputs)
        {
            Tiles =
            [
                new(Macro.Protein, "Protein", $"{r.Protein} g", r.ProteinWeightKg < r.WeightKg - 0.05
                    ? $"{Rate(r.ProteinPerKg)} g per kg of {Units.FormatWeight(r.ProteinWeightKg, inputs.Units)}"
                    : $"{Rate(r.ProteinPerKg)} g per kg"),
                new(Macro.Carbs, "Carbs", $"{r.Carbs} g", r.RaisedToMinimum ? "raised to the safe minimum" : $"{Rate(r.CarbsPerKg)} g per kg"),
                new(Macro.Fat, "Fat", $"{r.Fat} g", $"{Rate(r.FatPerKg)} g per kg"),
            ];
            RecommendedKcalText = CalorieGoals.K(r.Kcal);
            var person = inputs.Sex == Sex.Male ? "a man" : "a woman";
            var activity = inputs.Activity is { } a ? $", {ActivityLevels.DisplayName(a).ToLowerInvariant()}" : "";
            RecommendedNote = $"{VersusMaintenance(r.Kcal)} For {person} of {Units.FormatWeight(r.WeightKg, inputs.Units)}{activity}. " +
                              "Updates when your 2-week average weight moves a kilo.";
        }
        UpdateCustom();
    }

    private void UpdateCustom()
    {
        if (Parse(ProteinInput) is { } p && Parse(CarbsInput) is { } c && Parse(FatInput) is { } f)
        {
            var kcal = FoodRules.KcalFromMacros(p, c, f);
            CustomKcalText = "= " + CalorieGoals.K(kcal);
            CustomNote = $"Worked out from your macros. {VersusMaintenance(kcal)}".TrimEnd();
        }
        else
        {
            CustomKcalText = "—";
            CustomNote = "Enter all three to see your calorie goal.";
        }
    }

    /// <summary>"340 under your maintenance (2,350)." or empty without an estimate.</summary>
    private string VersusMaintenance(int kcal)
    {
        if (_inputs?.Maintenance is not { } m) return "";
        var diff = kcal - m;
        return diff == 0 ? $"Right at your maintenance ({CalorieGoals.K(m)})."
            : $"{CalorieGoals.K(Math.Abs(diff))} {(diff < 0 ? "under" : "over")} your maintenance ({CalorieGoals.K(m)}).";
    }

    /// <summary>Custom: copy the recommendation in as a starting point.</summary>
    [RelayCommand]
    private void StartFromRecommended()
    {
        if (Recommendation is not { } r) return;
        ProteinInput = r.Protein.ToString(CultureInfo.InvariantCulture);
        CarbsInput = r.Carbs.ToString(CultureInfo.InvariantCulture);
        FatInput = r.Fat.ToString(CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsSaving) return;
        Error = null;
        if (IsCustom && (Bad(ProteinInput) || Bad(CarbsInput) || Bad(FatInput)))
        {
            Error = "Enter the macros as whole numbers of grams, e.g. 150.";
            return;
        }
        IsSaving = true;
        try
        {
            var r = IsCustom
                ? await _calories.SetCustomMacrosAsync(_userId, GoalType, Parse(ProteinInput), Parse(CarbsInput), Parse(FatInput))
                : await _calories.UseRecommendedAsync(_userId, GoalType);
            if (!r.Success) { Error = r.Error; return; }
            _close();
            await _saved();
        }
        catch (Exception) { Error = "Could not save your calorie goal."; }
        finally { IsSaving = false; }
    }

    [RelayCommand] private void Close() => _close();
    [RelayCommand] private void AddBodyData() { _close(); _addBodyData(); }

    private static string Text(int? grams) => grams?.ToString(CultureInfo.InvariantCulture) ?? "";
    private static string Rate(double perKg) => perKg.ToString("0.0", CultureInfo.InvariantCulture);
    private static int? Parse(string text) => NumberParsing.TryParseWhole(text, out var n) ? n : null;
    private static bool Bad(string text) => !string.IsNullOrWhiteSpace(text) && Parse(text) is null;
}
