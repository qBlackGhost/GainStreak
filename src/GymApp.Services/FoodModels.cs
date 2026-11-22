using System.Collections.Generic;
using GymApp.Core;

namespace GymApp.Services;

/// <summary>A food as the search and the amount picker see it, saved or not. <see cref="FoodId"/> is null until the food
/// is copied into the user's own table the first time it's logged. <see cref="Problem"/> is set when the numbers fail
/// <see cref="FoodRules"/>: the screen opens the edit form instead of logging (saving makes a "Mine" copy).</summary>
public record FoodInfo(
    int? FoodId,
    FoodSource Source,
    string? SourceKey,
    string Name,
    string? Brand,
    bool IsLiquid,
    double KcalPer100,
    double? ProteinPer100,
    double? CarbsPer100,
    double? FatPer100,
    string? ServingName,
    double? ServingGrams,
    bool IsFavourite = false,
    string? Problem = null)
{
    public bool NeedsEdit => Problem is not null;
    public bool HasServing => ServingGrams is > 0;

    /// <summary>The line under the name: the brand, else where the food comes from.</summary>
    public string Origin => Brand ?? Source switch
    {
        FoodSource.Usda => "USDA",
        FoodSource.OpenFoodFacts => "Open Food Facts",
        _ => "My food"
    };

    /// <summary>"Open Food Facts" for its foods (the data's licence asks to name it), else null.</summary>
    public string? Attribution => Source == FoodSource.OpenFoodFacts ? "Open Food Facts" : null;

    /// <summary>kcal for one serving, or per 100 g when the food has no serving.</summary>
    public int Kcal => HasServing ? FoodRules.KcalFor(KcalPer100, ServingGrams!.Value) : (int)System.Math.Round(KcalPer100);
}

/// <summary>A food in the catalog file, with its household portions.</summary>
public record CatalogFood(long FdcId, string Name, double Kcal, double? Protein, double? Carbs, double? Fat,
    string? ServingName, double? ServingGrams, IReadOnlyList<CatalogPortion> Portions);

public record CatalogPortion(string Name, double Grams);

public enum LookupStatus { Found, NotFound, Offline, Failed }

/// <summary>One Open Food Facts barcode lookup. <see cref="Food"/> is set for <see cref="LookupStatus.Found"/>, possibly
/// with a <see cref="FoodInfo.Problem"/>.</summary>
public record BarcodeLookup(LookupStatus Status, FoodInfo? Food = null, string? Error = null);

/// <summary>An Open Food Facts name search. Offline: it could not be reached (the caller shows "Offline: showing foods on
/// this phone"). Error is set when it answered but failed.</summary>
public record OnlineSearch(bool Success, IReadOnlyList<FoodInfo> Foods, bool Offline = false, string? Error = null)
{
    public static OnlineSearch Empty { get; } = new(true, []);
    public static OnlineSearch Fail(bool offline, string error) => new(false, [], offline, error);
}
