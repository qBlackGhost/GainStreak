using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Core;
using GymApp.Services;

namespace GymApp.App.ViewModels;

/// <summary>
/// The New food form (Phase 26), also used to edit a food and to fix a product whose numbers look wrong: name, brand,
/// kcal and macros per 100 g (100 ml) <b>or</b> per serving, the serving's name and weight, and whether it is a liquid.
/// Per-serving numbers are converted to per 100 g when saved; the rules are <see cref="FoodRules"/>'s.
/// </summary>
public partial class FoodFormViewModel : ObservableObject
{
    public FoodFormViewModel(FoodInfo? from = null, string title = "New food", string? notice = null)
    {
        Title = title;
        Notice = notice;
        if (from is null) return;
        Original = from;
        Barcode = from.Source == FoodSource.OpenFoodFacts || from.Source == FoodSource.Mine ? from.SourceKey : null;
        Name = from.Name;
        Brand = from.Brand ?? "";
        IsLiquid = from.IsLiquid;
        // A product without energy comes in with 0: show an empty box instead, so it isn't saved as 0 by mistake
        KcalText = from.Problem == FoodRules.NoEnergy ? "" : Text(from.KcalPer100);
        ProteinText = Text(from.ProteinPer100);
        CarbsText = Text(from.CarbsPer100);
        FatText = Text(from.FatPer100);
        ServingName = from.ServingName ?? "";
        ServingGramsText = from.ServingGrams is { } g ? Text(g) : "";
    }

    /// <summary>The food being edited, or null for a new one.</summary>
    public FoodInfo? Original { get; }

    /// <summary>A barcode to save the food under (the scanned one), or null.</summary>
    public string? Barcode { get; init; }

    /// <summary>"Barcode 5941000010000" while a barcode is being saved with the food, else null.</summary>
    public string? BarcodeLine => string.IsNullOrEmpty(Barcode) ? null : $"Barcode {Barcode}";

    public string Title { get; }
    public string? Notice { get; }

    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string Brand { get; set; } = "";
    [ObservableProperty] public partial string KcalText { get; set; } = "";
    [ObservableProperty] public partial string ProteinText { get; set; } = "";
    [ObservableProperty] public partial string CarbsText { get; set; } = "";
    [ObservableProperty] public partial string FatText { get; set; } = "";
    [ObservableProperty] public partial string ServingName { get; set; } = "";
    [ObservableProperty] public partial string ServingGramsText { get; set; } = "";
    [ObservableProperty] public partial bool IsLiquid { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }

    /// <summary>0 = numbers are per 100 g (100 ml), 1 = per serving.</summary>
    [ObservableProperty] public partial int BasisIndex { get; set; }

    public string BasisUnit => IsLiquid ? "100 ml" : "100 g";
    public bool IsPerServing => BasisIndex == 1;
    public string Per100Label => $"Per {BasisUnit}";

    public bool IsPer100 => BasisIndex == 0;
    [RelayCommand] private void PickPer100() => BasisIndex = 0;
    [RelayCommand] private void PickPerServing() => BasisIndex = 1;

    partial void OnBasisIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsPerServing));
        OnPropertyChanged(nameof(IsPer100));
    }
    partial void OnIsLiquidChanged(bool value)
    {
        OnPropertyChanged(nameof(BasisUnit));
        OnPropertyChanged(nameof(Per100Label));
    }

    /// <summary>The input for <see cref="FoodService"/>, or null with <see cref="Error"/> set. The numbers become per 100 g.</summary>
    public FoodInput? TryBuild()
    {
        Error = null;
        if (!TryNumber(KcalText, "Calories", required: true, out var kcal) ||
            !TryNumber(ProteinText, "Protein", required: false, out var protein) ||
            !TryNumber(CarbsText, "Carbs", required: false, out var carbs) ||
            !TryNumber(FatText, "Fat", required: false, out var fat) ||
            !TryNumber(ServingGramsText, "The serving's weight", required: false, out var servingGrams))
            return null;

        if (servingGrams is not null && servingGrams <= 0) { Error = "The serving's weight must be more than 0."; return null; }
        if (IsPerServing)
        {
            if (servingGrams is not { } sg) { Error = $"Per serving needs the serving's weight in {(IsLiquid ? "ml" : "g")}."; return null; }
            kcal = FoodRules.To100(kcal!.Value, sg);
            protein = protein is { } p ? FoodRules.To100(p, sg) : null;
            carbs = carbs is { } c ? FoodRules.To100(c, sg) : null;
            fat = fat is { } f ? FoodRules.To100(f, sg) : null;
        }

        var input = new FoodInput(Name, Brand, IsLiquid, kcal, protein, carbs, fat,
            string.IsNullOrWhiteSpace(ServingName) ? null : ServingName.Trim(), servingGrams, Barcode);
        Error = FoodRules.CheckInput(input.Name, input.Brand, input.Kcal, input.Protein, input.Carbs, input.Fat, input.ServingGrams, input.ServingName);
        return Error is null ? input : null;
    }

    private bool TryNumber(string text, string what, bool required, out double? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            if (!required) return true;
            Error = what == "Calories" ? "Enter the calories." : $"Enter {what.ToLowerInvariant()}.";
            return false;
        }
        if (!FoodAmountViewModel.TryParse(text, out var v))
        {
            Error = $"{what} must be a number, e.g. 12.5.";
            return false;
        }
        value = v;
        return true;
    }

    private static string Text(double? v) => v is { } x ? Math.Round(x, 2).ToString("0.##", CultureInfo.InvariantCulture) : "";
}
