using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using GymApp.Core;
using GymApp.Services;

namespace GymApp.App.ViewModels;

public enum AmountKind { Servings, Metric, Imperial }

/// <summary>One choice in the amount picker's Servings / g / oz pill.</summary>
public partial class AmountOptionViewModel(AmountKind kind, string name) : ObservableObject
{
    public AmountKind Kind { get; } = kind;
    public string Name { get; } = name;
    [ObservableProperty] public partial bool IsSelected { get; set; }
}

/// <summary>
/// The amount picker (Phase 26): a pill of Servings (only when the food has a serving) / g (ml for a liquid) / oz (fl oz,
/// for imperial users), a number box, and the kcal and macros updating as the amount is typed. Whatever the unit, what
/// gets logged is grams (or ml) or a number of servings. Changing the unit keeps the same amount, in the new unit.
/// </summary>
public partial class FoodAmountViewModel : ObservableObject
{
    public FoodAmountViewModel(FoodInfo food, UnitSystem units, double? servings = null, double? grams = null)
    {
        Food = food;
        var options = new List<AmountOptionViewModel>();
        if (food.HasServing) options.Add(new AmountOptionViewModel(AmountKind.Servings, "Servings"));
        options.Add(new AmountOptionViewModel(AmountKind.Metric, food.IsLiquid ? "ml" : "g"));
        if (units == UnitSystem.Imperial) options.Add(new AmountOptionViewModel(AmountKind.Imperial, food.IsLiquid ? "fl oz" : "oz"));
        Options = options;

        // Start in servings when there is one (and the entry was in servings or is new), else in g / ml
        string text;
        int index;
        if (servings is > 0 && food.HasServing) { index = 0; text = Format(servings.Value); }
        else if (grams is > 0) { index = food.HasServing ? 1 : 0; text = Format(grams.Value); }
        else if (food.HasServing) { index = 0; text = "1"; }
        else { index = 0; text = "100"; }
        options[index].IsSelected = true;
        SelectedIndex = index;
        AmountText = text;
    }

    public FoodInfo Food { get; }
    public IReadOnlyList<AmountOptionViewModel> Options { get; }

    /// <summary>The selected option's position, for the sliding pill.</summary>
    [ObservableProperty] public partial int SelectedIndex { get; set; }

    [ObservableProperty] public partial string AmountText { get; set; } = "";

    [ObservableProperty] public partial string KcalText { get; set; } = "—";
    [ObservableProperty] public partial string ProteinText { get; set; } = "—";
    [ObservableProperty] public partial string CarbsText { get; set; } = "—";
    [ObservableProperty] public partial string FatText { get; set; } = "—";
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial bool IsValid { get; set; }

    /// <summary>The amount in grams (ml for a liquid), or null while the box doesn't hold a usable number.</summary>
    [ObservableProperty] public partial double? Grams { get; set; }

    public AmountKind Kind => Options[Math.Clamp(SelectedIndex, 0, Options.Count - 1)].Kind;
    public bool HasServing => Food.HasServing;

    /// <summary>"× 1 large (50 g each)" next to the box, in servings mode.</summary>
    public string ServingHint => Food.HasServing && Kind == AmountKind.Servings
        ? $"× {Food.ServingName} ({Format(Food.ServingGrams!.Value)} {(Food.IsLiquid ? "ml" : "g")} each)"
        : "";

    public string Unit => Kind switch
    {
        AmountKind.Servings => Food.HasServing ? "servings" : "",
        AmountKind.Metric => Food.IsLiquid ? "ml" : "g",
        _ => Food.IsLiquid ? "fl oz" : "oz"
    };

    /// <summary>What to hand to <see cref="FoodService"/>: servings as typed, everything else as grams.</summary>
    public (double Amount, AmountUnit Unit)? ToAmount()
    {
        if (!IsValid || Grams is not { } g) return null;
        if (Kind == AmountKind.Servings && TryParse(AmountText, out var servings)) return (servings, AmountUnit.Servings);
        return (g, AmountUnit.Grams);
    }

    public int? Kcal => Grams is { } g && IsValid ? FoodRules.KcalFor(Food.KcalPer100, g) : null;

    partial void OnAmountTextChanged(string value) => Recalculate();

    partial void OnSelectedIndexChanged(int oldValue, int newValue)
    {
        if (newValue < 0 || newValue >= Options.Count) return;
        // Keep the same amount in the new unit
        var oldGrams = Grams;
        foreach (var (o, i) in Options.Select((o, i) => (o, i))) o.IsSelected = i == newValue;
        if (oldGrams is { } g && IsValid)
            AmountText = Format(FromGrams(g, Options[newValue].Kind));
        else
            Recalculate();
        OnPropertyChanged(nameof(ServingHint));
        OnPropertyChanged(nameof(Unit));
        OnPropertyChanged(nameof(Kind));
    }

    private void Recalculate()
    {
        Error = null;
        Grams = null;
        IsValid = false;
        KcalText = ProteinText = CarbsText = FatText = "—";

        if (string.IsNullOrWhiteSpace(AmountText)) return;
        if (!TryParse(AmountText, out var amount))
        {
            Error = "Enter the amount as a number, e.g. 150.";
            return;
        }
        var grams = ToGrams(amount, Kind);
        if (grams is null) return;
        if (FoodRules.CheckAmount(grams.Value) is { } error) { Error = error; return; }

        Grams = grams;
        IsValid = true;
        KcalText = CalorieGoals.K(FoodRules.KcalFor(Food.KcalPer100, grams.Value));
        ProteinText = Macro(Food.ProteinPer100, grams.Value);
        CarbsText = Macro(Food.CarbsPer100, grams.Value);
        FatText = Macro(Food.FatPer100, grams.Value);
        OnPropertyChanged(nameof(Kcal));
    }

    private double? ToGrams(double amount, AmountKind kind) => kind switch
    {
        AmountKind.Servings => Food.ServingGrams is { } s ? amount * s : null,
        AmountKind.Metric => amount,
        _ => Food.IsLiquid ? Units.FlOzToMl(amount) : Units.OzToGrams(amount)
    };

    private double FromGrams(double grams, AmountKind kind) => kind switch
    {
        AmountKind.Servings => grams / Food.ServingGrams!.Value,
        AmountKind.Metric => grams,
        _ => Food.IsLiquid ? Units.MlToFlOz(grams) : Units.GramsToOz(grams)
    };

    private static string Macro(double? per100, double grams) =>
        per100 is { } v ? FoodRules.Scale(v, grams).ToString("0.#", CultureInfo.InvariantCulture) : "—";

    public static string Format(double v) => Math.Round(v, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>A number with a point or a comma, zero or more. (Whole 0 is allowed here; <see cref="FoodRules.CheckAmount"/> refuses it.)</summary>
    public static bool TryParse(string? text, out double value)
    {
        value = 0;
        var t = (text ?? "").Trim().Replace(',', '.');
        return t.Length > 0 && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
               && !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
    }
}
