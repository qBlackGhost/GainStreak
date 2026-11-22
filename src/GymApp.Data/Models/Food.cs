using System;
using GymApp.Core;

namespace GymApp.Data.Models;

/// <summary>A food the user logged or saved (Phase 26). Catalog and Open Food Facts foods are copied here the first time
/// they're used, so recents, favourites and entries never depend on the catalog file or the network.</summary>
public class Food
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public FoodSource Source { get; set; }
    public string? SourceKey { get; set; }              // USDA fdc_id or the barcode; null for a food without a barcode
    public string Name { get; set; } = "";
    public string? Brand { get; set; }
    public bool IsLiquid { get; set; }                  // per 100 ml instead of per 100 g
    public double KcalPer100 { get; set; }
    public double? ProteinPer100 { get; set; }
    public double? CarbsPer100 { get; set; }
    public double? FatPer100 { get; set; }
    public string? ServingName { get; set; }            // "1 slice"
    public double? ServingGrams { get; set; }           // 30
    public bool IsFavourite { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public int UseCount { get; set; }
    public bool IsHidden { get; set; }                  // a deleted "Mine" food that entries still point to
}

/// <summary>One thing eaten (Phase 26). The numbers are a snapshot of the food at the time, so editing a food later never
/// changes past days. A Quick add has no food, only a label and the kcal.</summary>
public class FoodEntry
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public DateOnly Date { get; set; }
    public Meal Meal { get; set; }
    public int? FoodId { get; set; }
    public Food? Food { get; set; }
    public string? Label { get; set; }                  // a Quick add's text
    public double? Grams { get; set; }                  // g, or ml for a liquid
    public double? Servings { get; set; }               // set when the user entered servings
    public int Kcal { get; set; }
    public double? Protein { get; set; }
    public double? Carbs { get; set; }
    public double? Fat { get; set; }
    public DateTime CreatedAt { get; set; }
}
