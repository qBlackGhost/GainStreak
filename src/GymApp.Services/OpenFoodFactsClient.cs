using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GymApp.Core;

namespace GymApp.Services;

/// <summary>Open Food Facts, read-only (Phase 26): barcode lookup and name search. No account, nothing sent but the barcode or
/// the typed text. Each endpoint has its own <see cref="RequestThrottle"/>, 10 seconds is the longest wait (then it counts
/// as offline, like the other clients), and it never throws for network trouble. Register as a singleton.
/// Database ODbL, contents DbCL; product images aren't used.</summary>
public class OpenFoodFactsClient
{
    public const string ProductUrl = "https://world.openfoodfacts.org/api/v2/product";
    public const string SearchUrl = "https://search.openfoodfacts.org/search";
    /// <summary>The fields we keep from a product (the API returns far more).</summary>
    public const string ProductFields = "code,product_name,product_name_{lang},brands,nutriments,serving_size,serving_quantity,quantity";
    public const string SearchFields = "code,product_name,brands,nutriments,serving_size,serving_quantity,quantity";
    public const int MinSearchLength = 3;
    public const int SearchPageSize = 20;

    private readonly HttpClient _http;
    private readonly RequestThrottle _productThrottle;
    private readonly RequestThrottle _searchThrottle;

    /// <summary>Open Food Facts allows 15 product reads a minute per user (about one every 4 s) and 10 name searches; the
    /// search-a-licious API we use has no published limit, so it is spaced conservatively (26a).</summary>
    public OpenFoodFactsClient() : this(null, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(2)) { }

    /// <param name="handler">For tests: answers instead of the real API.</param>
    public OpenFoodFactsClient(HttpMessageHandler? handler, TimeSpan productInterval, TimeSpan searchInterval, TimeSpan? timeout = null)
    {
        _http = AppHttp.CreateClient(timeout ?? TimeSpan.FromSeconds(10), handler, AppHttp.WithSite(AppHttp.UserAgent));
        _productThrottle = new RequestThrottle(productInterval);
        _searchThrottle = new RequestThrottle(searchInterval);
    }

    /// <summary>EAN-8 / UPC-E / UPC-A / EAN-13 / GTIN-14: digits only, 8 to 14 of them.</summary>
    public static bool IsBarcode(string? text)
    {
        if (text is not { Length: >= 8 and <= 14 }) return false;
        foreach (var ch in text) if (ch is < '0' or > '9') return false;
        return true;
    }

    public async Task<BarcodeLookup> LookupAsync(string barcode, string? language = null, CancellationToken ct = default)
    {
        barcode = (barcode ?? "").Trim();
        if (!IsBarcode(barcode)) return new(LookupStatus.NotFound, Error: "That is not a barcode number.");

        var lang = CleanLanguage(language);
        var fields = ProductFields.Replace("{lang}", lang ?? "en");
        var url = $"{ProductUrl}/{barcode}?fields={Uri.EscapeDataString(fields).Replace("%2C", ",")}";
        var (json, failure) = await GetAsync(_productThrottle, url, ct);
        if (json is null) return failure!;

        using (json)
        {
            var root = json.RootElement;
            // v2 answers 200 with status 0 for an unknown barcode (and 404 in some cases, handled in GetAsync)
            if (!root.TryGetProperty("product", out var product) || product.ValueKind != JsonValueKind.Object)
                return new(LookupStatus.NotFound);
            var food = Map(product, barcode, lang);
            return food is null ? new(LookupStatus.NotFound) : new(LookupStatus.Found, food);
        }
    }

    public async Task<OnlineSearch> SearchAsync(string text, string? language = null, CancellationToken ct = default)
    {
        text = (text ?? "").Trim();
        if (text.Length < MinSearchLength) return OnlineSearch.Empty;

        var lang = CleanLanguage(language);
        var url = $"{SearchUrl}?q={Uri.EscapeDataString(text)}&page_size={SearchPageSize}&fields={SearchFields.Replace(",", "%2C")}" +
                  (lang is null ? "" : $"&langs={lang}");
        var (json, failure) = await GetAsync(_searchThrottle, url, ct);
        if (json is null) return OnlineSearch.Fail(failure!.Status == LookupStatus.Offline, failure.Error ?? "Open Food Facts did not answer.");

        using (json)
        {
            var foods = new List<FoodInfo>();
            if (json.RootElement.TryGetProperty("hits", out var hits) && hits.ValueKind == JsonValueKind.Array)
                foreach (var hit in hits.EnumerateArray())
                    if (hit.ValueKind == JsonValueKind.Object && hit.TryGetProperty("code", out var code) &&
                        code.ValueKind == JsonValueKind.String && IsBarcode(code.GetString()) &&
                        Map(hit, code.GetString()!, lang) is { } food)
                        foods.Add(food);
            return new OnlineSearch(true, foods);
        }
    }

    private async Task<(JsonDocument?, BarcodeLookup?)> GetAsync(RequestThrottle throttle, string url, CancellationToken ct)
    {
        try
        {
            return await throttle.RunAsync(async () =>
            {
                using var response = await _http.GetAsync(url, ct);
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return ((JsonDocument?)null, (BarcodeLookup?)new BarcodeLookup(LookupStatus.NotFound));
                if (!response.IsSuccessStatusCode)
                    return (null, new BarcodeLookup(LookupStatus.Failed, Error: response.StatusCode == HttpStatusCode.TooManyRequests
                        ? "Open Food Facts is busy. Try again in a minute."
                        : "Open Food Facts could not answer right now."));
                var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                return (doc, null);
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return (null, new BarcodeLookup(LookupStatus.Offline, Error: "Open Food Facts took too long.")); }   // the 10 s timeout
        catch (HttpRequestException) { return (null, new BarcodeLookup(LookupStatus.Offline, Error: "No connection.")); }
        catch (JsonException) { return (null, new BarcodeLookup(LookupStatus.Failed, Error: "Open Food Facts sent something we could not read.")); }
    }

    /// <summary>A product (or search hit) as a food. Null when it has no name to show. The numbers are checked with
    /// <see cref="FoodRules"/>: a failing product still comes back, with its Problem set.</summary>
    internal static FoodInfo? Map(JsonElement p, string barcode, string? lang)
    {
        var name = Text(p, lang is null ? null : $"product_name_{lang}") ?? Text(p, "product_name");
        var brand = FirstBrand(p);
        if (name is null && brand is null) return null;
        name = Trim(name ?? brand!, FoodRules.MaxNameLength);

        double? kcal = null, protein = null, carbs = null, fat = null;
        if (p.TryGetProperty("nutriments", out var n) && n.ValueKind == JsonValueKind.Object)
        {
            kcal = Number(n, "energy-kcal_100g");
            // kJ only: energy-kj_100g, or energy_100g, which Open Food Facts keeps in kJ
            if (kcal is null && (Number(n, "energy-kj_100g") ?? Number(n, "energy_100g")) is { } kj) kcal = FoodRules.KjToKcal(kj);
            protein = Number(n, "proteins_100g");
            carbs = Number(n, "carbohydrates_100g");
            fat = Number(n, "fat_100g");
        }

        var servingGrams = Number(p, "serving_quantity");
        var servingText = Text(p, "serving_size");
        if (servingGrams is null or <= 0 && servingText is not null) servingGrams = GramsIn(servingText);
        if (servingGrams is <= 0) servingGrams = null;
        string? servingName = null;
        if (servingGrams is not null)
        {
            // "1 portion (13.8 g)" and "30g" read fine as they are; a bare number gets "1 serving"
            servingName = servingText is { Length: > 0 and <= 80 } && servingText.Any(char.IsLetter) ? servingText : "1 serving";
            if (CheckServing(servingGrams) is not null) { servingGrams = null; servingName = null; }
        }

        var isLiquid = IsLiquid(Text(p, "quantity"), servingText);
        var problem = FoodRules.CheckPer100(kcal, protein, carbs, fat) ?? FoodRules.CheckHasMacros(kcal, protein, carbs, fat) ?? FoodRules.CheckServing(servingGrams);
        return new FoodInfo(null, FoodSource.OpenFoodFacts, barcode, name, brand, isLiquid,
            Math.Round(kcal ?? 0, 1), Round(protein), Round(carbs), Round(fat), servingName, servingGrams, Problem: problem);
    }

    private static string? CheckServing(double? grams) => FoodRules.CheckServing(grams);

    private static double? Round(double? v) => v is null ? null : Math.Round(v.Value, 2);

    // Open Food Facts sends numbers as numbers or as strings, and leaves fields out when it has no value
    private static double? Number(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var e)) return null;
        switch (e.ValueKind)
        {
            case JsonValueKind.Number: return e.TryGetDouble(out var d) ? d : null;
            case JsonValueKind.String when double.TryParse(e.GetString()?.Replace(',', '.'), NumberStyles.Float,
                                               CultureInfo.InvariantCulture, out var s): return s;
            default: return null;
        }
    }

    private static string? Text(JsonElement parent, string? name)
    {
        if (name is null || !parent.TryGetProperty(name, out var e) || e.ValueKind != JsonValueKind.String) return null;
        var t = e.GetString()?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    // The product endpoint sends brands as "Ferrero, Nutella", the search API as a list
    private static string? FirstBrand(JsonElement p)
    {
        if (!p.TryGetProperty("brands", out var b)) return null;
        string? first = b.ValueKind switch
        {
            JsonValueKind.String => b.GetString()?.Split(',')[0],
            JsonValueKind.Array when b.GetArrayLength() > 0 && b[0].ValueKind == JsonValueKind.String => b[0].GetString(),
            _ => null
        };
        first = first?.Trim();
        return string.IsNullOrEmpty(first) ? null : Trim(first, FoodRules.MaxBrandLength);
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd();

    private static bool IsLiquid(string? quantity, string? serving)
    {
        static bool Ml(string? t) => t is not null && System.Text.RegularExpressions.Regex.IsMatch(t, @"\d\s*(ml|cl|dl|l|fl\.?\s*oz)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return Ml(quantity) || Ml(serving);
    }

    /// <summary>The grams in "1 portion (13.8 g)" or "30g"; null when there is no weight. Millilitres count as grams.</summary>
    internal static double? GramsIn(string text)
    {
        var m = System.Text.RegularExpressions.Regex.Match(text, @"(\d+(?:[.,]\d+)?)\s*(g|gr|grams?|ml)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success && double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var g)
            ? g : null;
    }

    private static string? CleanLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return null;
        var l = language.Trim().ToLowerInvariant();
        return l.Length == 2 && char.IsLetter(l[0]) && char.IsLetter(l[1]) ? l : null;
    }
}
