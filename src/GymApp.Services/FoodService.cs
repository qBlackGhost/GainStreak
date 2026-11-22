using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

public enum AmountUnit { Servings, Grams }

/// <summary>One entry of the food diary. <see cref="Name"/> is the food's name, or the Quick add's label.</summary>
public record FoodEntryInfo(int Id, DateOnly Date, Meal Meal, int? FoodId, string Name, string? Brand, double? Grams, double? Servings,
    string? ServingName, bool IsLiquid, int Kcal, double? Protein, double? Carbs, double? Fat)
{
    /// <summary>A typed number with no food. (An entry whose food row is gone still has its grams, so it isn't one.)</summary>
    public bool IsQuickAdd => FoodId is null && Grams is null;
}

/// <summary>A day's food. <see cref="TotalOnly"/>: the day has a calorie total but no entries (logged before the food log, or
/// as a typed total); <see cref="Kcal"/> is then that total.</summary>
public record FoodDay(DateOnly Date, IReadOnlyList<FoodEntryInfo> Entries, int Kcal, double Protein, double Carbs, double Fat, bool TotalOnly)
{
    public IEnumerable<FoodEntryInfo> In(Meal meal) => Entries.Where(e => e.Meal == meal);
    public int KcalIn(Meal meal) => In(meal).Sum(e => e.Kcal);
}

public record FoodResult(bool Success, string? Error = null, FoodEntryInfo? Entry = null, FoodInfo? Food = null, PointsSync? Points = null)
{
    public static FoodResult Fail(string error) => new(false, error);
}

/// <summary>What the New food form gives: numbers per 100 g (100 ml for a liquid). A form that asks per serving converts
/// with <see cref="FoodRules.To100"/> first.</summary>
public record FoodInput(string Name, string? Brand, bool IsLiquid, double? Kcal, double? Protein, double? Carbs, double? Fat,
    string? ServingName, double? ServingGrams, string? Barcode = null);

/// <summary>The food log (Phase 26): finding foods, logging them as entries and keeping the day's calories in step.
/// Food data stays on this phone. The day's total is still <see cref="CalorieLog.CaloriesConsumed"/>: every add, edit or
/// delete of an entry recomputes it and saves it through <see cref="CalorieService.LogTodayAsync"/> (which snapshots the goal
/// and syncs points). Entries can be added, edited and deleted for today only.</summary>
public class FoodService(IDbContextFactory<AppDbContext> dbFactory, FoodCatalog catalog, OpenFoodFactsClient openFoodFacts,
    CalorieService calories, PointsService points)
{
    public const int RecentCount = 20;
    public const int LocalResultLimit = 25;
    public const string TodayOnly = "Only today's food can be changed.";

    // ---------------------------------------------------------------- search

    /// <summary>The user's own foods matching the text (favourites first, then most used), then the USDA catalog. Foods
    /// already saved appear once, as the saved copy. Offline-safe: nothing here needs the network.</summary>
    public async Task<IReadOnlyList<FoodInfo>> SearchLocalAsync(int userId, string text)
    {
        var words = Words(text);
        if (words.Count == 0) return [];

        await using var db = await dbFactory.CreateDbContextAsync();
        IQueryable<Food> q = db.Foods.AsNoTracking().Where(f => f.UserId == userId && !f.IsHidden);
        foreach (var w in words)
        {
            var like = $"%{EscapeLike(w)}%";
            q = q.Where(f => EF.Functions.Like(f.Name, like, "\\") || (f.Brand != null && EF.Functions.Like(f.Brand, like, "\\")));
        }
        var mine = (await q.OrderByDescending(f => f.IsFavourite).ThenByDescending(f => f.UseCount).ThenBy(f => f.Name)
            .Take(LocalResultLimit).ToListAsync()).Select(ToInfo).ToList();

        var saved = new HashSet<(FoodSource, string?)>(mine.Select(f => (f.Source, f.SourceKey)));
        var result = new List<FoodInfo>(mine);
        foreach (var c in await catalog.SearchAsync(text, LocalResultLimit))
        {
            if (saved.Contains((FoodSource.Usda, c.FdcId.ToString()))) continue;
            result.Add(ToInfo(c));
        }
        return result;
    }

    /// <summary>Open Food Facts name search (3 characters or more). Hits the user already has come back as their saved copy.
    /// Never throws: <see cref="OnlineSearch.Offline"/> says it could not be reached.</summary>
    public async Task<OnlineSearch> SearchOnlineAsync(int userId, string text, string? language = null, CancellationToken ct = default)
    {
        var online = await openFoodFacts.SearchAsync(text, language, ct);
        if (!online.Success || online.Foods.Count == 0) return online;

        var keys = online.Foods.Select(f => f.SourceKey!).Distinct().ToList();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var have = await db.Foods.AsNoTracking()
            .Where(f => f.UserId == userId && !f.IsHidden && f.SourceKey != null && keys.Contains(f.SourceKey) && f.Source != FoodSource.Usda)
            .ToListAsync(ct);
        var foods = online.Foods
            .Select(f => PreferSaved(have, f.SourceKey!) is { } mine ? ToInfo(mine) : f)
            .ToList();
        return online with { Foods = foods };
    }

    /// <summary>Local results, then Open Food Facts' appended when online and the text is long enough, de-duplicated by
    /// (source, key). The screen runs the two halves itself (local at once, online after a pause); this is the same thing in
    /// one call.</summary>
    public async Task<(IReadOnlyList<FoodInfo> Foods, OnlineSearch Online)> SearchAsync(int userId, string text, string? language = null,
        CancellationToken ct = default)
    {
        var local = await SearchLocalAsync(userId, text);
        var online = await SearchOnlineAsync(userId, text, language, ct);
        var seen = new HashSet<(FoodSource, string?)>(local.Select(f => (f.Source, f.SourceKey)));
        var foods = new List<FoodInfo>(local);
        foreach (var f in online.Foods)
            if (f.SourceKey is null || seen.Add((f.Source, f.SourceKey))) foods.Add(f);
        return (foods, online);
    }

    /// <summary>The last 20 distinct foods logged, newest first.</summary>
    public async Task<IReadOnlyList<FoodInfo>> RecentsAsync(int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var foods = await db.Foods.AsNoTracking()
            .Where(f => f.UserId == userId && !f.IsHidden && f.LastUsedAt != null)
            .OrderByDescending(f => f.LastUsedAt).Take(RecentCount).ToListAsync();
        return foods.Select(ToInfo).ToList();
    }

    public async Task<IReadOnlyList<FoodInfo>> FavouritesAsync(int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var foods = await db.Foods.AsNoTracking()
            .Where(f => f.UserId == userId && !f.IsHidden && f.IsFavourite).OrderBy(f => f.Name).ToListAsync();
        return foods.Select(ToInfo).ToList();
    }

    /// <summary>A scanned or typed barcode: the user's own food with it first (their edit wins), then Open Food Facts.</summary>
    public async Task<BarcodeLookup> LookupBarcodeAsync(int userId, string barcode, string? language = null, CancellationToken ct = default)
    {
        barcode = (barcode ?? "").Trim();
        if (!OpenFoodFactsClient.IsBarcode(barcode)) return new(LookupStatus.NotFound, Error: "That is not a barcode number.");

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var have = await db.Foods.AsNoTracking()
                .Where(f => f.UserId == userId && !f.IsHidden && f.SourceKey == barcode && f.Source != FoodSource.Usda).ToListAsync(ct);
            if (PreferSaved(have, barcode) is { } saved) return new(LookupStatus.Found, ToInfo(saved));
        }
        return await openFoodFacts.LookupAsync(barcode, language, ct);
    }

    /// <summary>One of the user's saved foods (to edit an entry's amount), or null when it is gone.</summary>
    public async Task<FoodInfo?> GetFoodAsync(int userId, int foodId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.Foods.AsNoTracking().FirstOrDefaultAsync(f => f.Id == foodId && f.UserId == userId);
        return row is null ? null : ToInfo(row);
    }

    // ---------------------------------------------------------------- the day

    public async Task<FoodDay> GetDayAsync(int userId, DateOnly date)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var entries = (await db.FoodEntries.AsNoTracking().Include(e => e.Food)
                .Where(e => e.UserId == userId && e.Date == date).ToListAsync())
            .OrderBy(e => e.Meal).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .Select(ToEntry).ToList();
        if (entries.Count > 0)
            return new FoodDay(date, entries, entries.Sum(e => e.Kcal), Sum(entries, e => e.Protein), Sum(entries, e => e.Carbs),
                Sum(entries, e => e.Fat), TotalOnly: false);

        var total = await db.CalorieLogs.AsNoTracking().Where(c => c.UserId == userId && c.Date == date)
            .Select(c => (int?)c.CaloriesConsumed).FirstOrDefaultAsync();
        return new FoodDay(date, entries, total ?? 0, 0, 0, 0, TotalOnly: total is not null);
    }

    // ---------------------------------------------------------------- logging

    /// <summary>Logs a food for today in a meal. The food is copied into the user's foods the first time (a food whose numbers
    /// fail <see cref="FoodRules"/> must be edited first). The entry snapshots the kcal and macros.</summary>
    public async Task<FoodResult> LogAsync(int userId, FoodInfo food, Meal meal, double amount, AmountUnit unit, DateOnly? today = null)
    {
        var day = today ?? AppClock.Today;
        if (food.Problem is { } problem) return FoodResult.Fail(problem);
        if (!Enum.IsDefined(meal)) return FoodResult.Fail("Pick a meal.");
        var grams = ToGrams(amount, unit, food.ServingGrams);
        if (grams is null) return FoodResult.Fail("This food has no serving size. Enter grams instead.");
        if (FoodRules.CheckAmount(grams.Value) is { } amountError) return FoodResult.Fail(amountError);

        FoodEntry entry;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            if (await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId) is false) return FoodResult.Fail("User not found.");
            var row = await EnsureSavedAsync(db, userId, food);
            if (row is null) return FoodResult.Fail("Could not find that food.");

            var kcal = FoodRules.KcalFor(row.KcalPer100, grams.Value);
            if (await TotalErrorAsync(db, userId, day, kcal) is { } tooMuch) return FoodResult.Fail(tooMuch);

            entry = new FoodEntry
            {
                UserId = userId, Date = day, Meal = meal, Food = row, Grams = Math.Round(grams.Value, 1),
                Servings = unit == AmountUnit.Servings ? Math.Round(amount, 2) : null,
                Kcal = kcal, Protein = Macro(row.ProteinPer100, grams.Value), Carbs = Macro(row.CarbsPer100, grams.Value),
                Fat = Macro(row.FatPer100, grams.Value), CreatedAt = AppClock.Now
            };
            await MarkLoggedAsTotalAsync(db, userId, day);
            db.FoodEntries.Add(entry);
            row.LastUsedAt = AppClock.Now;
            row.UseCount++;
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException) { return FoodResult.Fail("Could not save your food."); }
            food = ToInfo(row);
        }
        return await FinishAsync(userId, day, entry.Id, food);
    }

    /// <summary>The quick "+": logs the amount last used for this food (its servings if it was logged in servings), else one
    /// serving, else 100 g.</summary>
    public async Task<FoodResult> QuickLogAsync(int userId, FoodInfo food, Meal meal, DateOnly? today = null)
    {
        double? lastGrams = null, lastServings = null;
        if (food.FoodId is { } id)
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var last = await db.FoodEntries.AsNoTracking().Where(e => e.UserId == userId && e.FoodId == id)
                .OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).FirstOrDefaultAsync();
            lastGrams = last?.Grams;
            lastServings = last?.Servings;
        }
        if (lastServings is > 0 && food.HasServing) return await LogAsync(userId, food, meal, lastServings.Value, AmountUnit.Servings, today);
        if (lastGrams is > 0) return await LogAsync(userId, food, meal, lastGrams.Value, AmountUnit.Grams, today);
        return food.HasServing
            ? await LogAsync(userId, food, meal, 1, AmountUnit.Servings, today)
            : await LogAsync(userId, food, meal, 100, AmountUnit.Grams, today);
    }

    /// <summary>A typed number: "Quick add · 450 kcal" in the meal, no food, an optional label.</summary>
    public async Task<FoodResult> QuickAddAsync(int userId, int kcal, string? label, Meal meal, DateOnly? today = null)
    {
        var day = today ?? AppClock.Today;
        if (CalorieGoals.CheckConsumed(kcal) is { } error) return FoodResult.Fail(error);
        if (!Enum.IsDefined(meal)) return FoodResult.Fail("Pick a meal.");
        label = CleanLabel(label);
        if (label is { Length: > FoodRules.MaxLabelLength }) return FoodResult.Fail($"The label can be at most {FoodRules.MaxLabelLength} characters.");

        FoodEntry entry;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            if (await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId) is false) return FoodResult.Fail("User not found.");
            if (await TotalErrorAsync(db, userId, day, kcal) is { } tooMuch) return FoodResult.Fail(tooMuch);
            entry = new FoodEntry { UserId = userId, Date = day, Meal = meal, Label = label, Kcal = kcal, CreatedAt = AppClock.Now };
            await MarkLoggedAsTotalAsync(db, userId, day);
            db.FoodEntries.Add(entry);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException) { return FoodResult.Fail("Could not save your calories."); }
        }
        return await FinishAsync(userId, day, entry.Id, null);
    }

    /// <summary>Changes the meal and amount of today's food entry. The numbers come from the food as it is now, or, when the
    /// food is gone, from the entry's own numbers scaled to the new amount.</summary>
    public async Task<FoodResult> EditEntryAsync(int userId, int entryId, Meal meal, double amount, AmountUnit unit, DateOnly? today = null)
    {
        var day = today ?? AppClock.Today;
        if (!Enum.IsDefined(meal)) return FoodResult.Fail("Pick a meal.");

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var entry = await db.FoodEntries.Include(e => e.Food).FirstOrDefaultAsync(e => e.Id == entryId && e.UserId == userId);
            if (entry is null) return FoodResult.Fail("That entry is gone.");
            if (entry.Date != day) return FoodResult.Fail(TodayOnly);
            if (entry.FoodId is null && entry.Food is null && entry.Grams is null)
                return FoodResult.Fail("Quick adds are edited by their calories.");

            var servingGrams = entry.Food?.ServingGrams;
            var grams = ToGrams(amount, unit, servingGrams);
            if (grams is null) return FoodResult.Fail("This food has no serving size. Enter grams instead.");
            if (FoodRules.CheckAmount(grams.Value) is { } amountError) return FoodResult.Fail(amountError);

            int kcal; double? protein, carbs, fat;
            if (entry.Food is { } f)
            {
                kcal = FoodRules.KcalFor(f.KcalPer100, grams.Value);
                protein = Macro(f.ProteinPer100, grams.Value); carbs = Macro(f.CarbsPer100, grams.Value); fat = Macro(f.FatPer100, grams.Value);
            }
            else
            {
                var oldGrams = entry.Grams!.Value;
                var ratio = grams.Value / oldGrams;
                kcal = (int)Math.Round(entry.Kcal * ratio, MidpointRounding.AwayFromZero);
                protein = Scaled(entry.Protein, ratio); carbs = Scaled(entry.Carbs, ratio); fat = Scaled(entry.Fat, ratio);
            }
            if (await TotalErrorAsync(db, userId, day, kcal - entry.Kcal) is { } tooMuch) return FoodResult.Fail(tooMuch);

            entry.Meal = meal;
            entry.Grams = Math.Round(grams.Value, 1);
            entry.Servings = unit == AmountUnit.Servings ? Math.Round(amount, 2) : null;
            entry.Kcal = kcal; entry.Protein = protein; entry.Carbs = carbs; entry.Fat = fat;
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException) { return FoodResult.Fail("Could not save your food."); }
        }
        return await FinishAsync(userId, day, entryId, null);
    }

    /// <summary>Changes a Quick add's calories, label and meal.</summary>
    public async Task<FoodResult> EditQuickAddAsync(int userId, int entryId, Meal meal, int kcal, string? label, DateOnly? today = null)
    {
        var day = today ?? AppClock.Today;
        if (CalorieGoals.CheckConsumed(kcal) is { } error) return FoodResult.Fail(error);
        if (!Enum.IsDefined(meal)) return FoodResult.Fail("Pick a meal.");
        label = CleanLabel(label);
        if (label is { Length: > FoodRules.MaxLabelLength }) return FoodResult.Fail($"The label can be at most {FoodRules.MaxLabelLength} characters.");

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var entry = await db.FoodEntries.FirstOrDefaultAsync(e => e.Id == entryId && e.UserId == userId);
            if (entry is null) return FoodResult.Fail("That entry is gone.");
            if (entry.Date != day) return FoodResult.Fail(TodayOnly);
            if (entry.FoodId is not null || entry.Grams is not null) return FoodResult.Fail("Only a Quick add can be edited by its calories.");
            if (await TotalErrorAsync(db, userId, day, kcal - entry.Kcal) is { } tooMuch) return FoodResult.Fail(tooMuch);

            entry.Meal = meal; entry.Kcal = kcal; entry.Label = label;
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException) { return FoodResult.Fail("Could not save your calories."); }
        }
        return await FinishAsync(userId, day, entryId, null);
    }

    public async Task<FoodResult> DeleteEntryAsync(int userId, int entryId, DateOnly? today = null)
    {
        var day = today ?? AppClock.Today;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var entry = await db.FoodEntries.FirstOrDefaultAsync(e => e.Id == entryId && e.UserId == userId);
            if (entry is null) return FoodResult.Fail("That entry is gone.");
            if (entry.Date != day) return FoodResult.Fail(TodayOnly);
            db.FoodEntries.Remove(entry);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException) { return FoodResult.Fail("Could not delete that."); }
        }
        return await RollUpAsync(userId, day);
    }

    // ---------------------------------------------------------------- the user's foods

    /// <summary>A new "Mine" food. With a barcode, the next scan finds it. A barcode the user already saved is refused,
    /// unless that food was deleted (then it comes back with the new numbers).</summary>
    public async Task<FoodResult> CreateFoodAsync(int userId, FoodInput input)
    {
        if (Check(input) is { } error) return FoodResult.Fail(error);
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId) is false) return FoodResult.Fail("User not found.");

        var key = CleanBarcode(input.Barcode);
        Food? row = null;
        if (key is not null)
        {
            row = await db.Foods.FirstOrDefaultAsync(f => f.UserId == userId && f.Source == FoodSource.Mine && f.SourceKey == key);
            if (row is { IsHidden: false }) return FoodResult.Fail("You already have a food with this barcode.");
        }
        if (row is null)
        {
            row = new Food { UserId = userId, Source = FoodSource.Mine, SourceKey = key };
            db.Foods.Add(row);
        }
        Apply(row, input);
        row.IsHidden = false;
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return FoodResult.Fail("Could not save that food."); }
        return new FoodResult(true, Food: ToInfo(row));
    }

    /// <summary>Saves an edit of a food. A "Mine" food is changed in place (past entries keep their numbers); any other food
    /// stays as it is and the edit becomes a "Mine" copy, which keeps the barcode so the next scan finds the corrected one.</summary>
    public async Task<FoodResult> SaveFoodEditAsync(int userId, FoodInfo original, FoodInput input)
    {
        if (Check(input) is { } error) return FoodResult.Fail(error);
        await using var db = await dbFactory.CreateDbContextAsync();

        Food? row = null;
        if (original is { Source: FoodSource.Mine, FoodId: { } id })
        {
            row = await db.Foods.FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId);
            if (row is null) return FoodResult.Fail("That food is gone.");
        }
        else
        {
            var key = original.Source == FoodSource.OpenFoodFacts ? original.SourceKey : CleanBarcode(input.Barcode);
            if (key is not null)
                row = await db.Foods.FirstOrDefaultAsync(f => f.UserId == userId && f.Source == FoodSource.Mine && f.SourceKey == key);
            if (row is null)
            {
                row = new Food { UserId = userId, Source = FoodSource.Mine, SourceKey = key };
                if (original.FoodId is { } savedId)   // the saved copy of the original keeps its favourite star
                    row.IsFavourite = await db.Foods.AsNoTracking().Where(f => f.Id == savedId && f.UserId == userId).Select(f => f.IsFavourite).FirstOrDefaultAsync();
                db.Foods.Add(row);
            }
        }
        Apply(row, input);
        row.IsHidden = false;
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return FoodResult.Fail("Could not save that food."); }
        return new FoodResult(true, Food: ToInfo(row));
    }

    /// <summary>Deletes one of the user's own foods. Entries that point to it keep their numbers; the food is then only
    /// hidden. Catalog and Open Food Facts foods can't be deleted (unstar them instead).</summary>
    public async Task<FoodResult> DeleteFoodAsync(int userId, int foodId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.Foods.FirstOrDefaultAsync(f => f.Id == foodId && f.UserId == userId);
        if (row is null) return FoodResult.Fail("That food is gone.");
        if (row.Source != FoodSource.Mine) return FoodResult.Fail("Only your own foods can be deleted.");

        if (await db.FoodEntries.AnyAsync(e => e.FoodId == foodId))
        {
            row.IsHidden = true;
            row.IsFavourite = false;
        }
        else db.Foods.Remove(row);
        await db.SaveChangesAsync();
        return new FoodResult(true);
    }

    /// <summary>Stars or unstars a food, saving it into the user's foods first if it is still a catalog or Open Food Facts hit.</summary>
    public async Task<FoodResult> SetFavouriteAsync(int userId, FoodInfo food, bool favourite)
    {
        if (food.Problem is { } problem) return FoodResult.Fail(problem);
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await EnsureSavedAsync(db, userId, food);
        if (row is null) return FoodResult.Fail("Could not find that food.");
        row.IsFavourite = favourite;
        await db.SaveChangesAsync();
        return new FoodResult(true, Food: ToInfo(row));
    }

    // ---------------------------------------------------------------- the roll-up

    /// <summary>Sets the day's <see cref="CalorieLog"/> to the sum of its entries and runs the points sync. With no entries
    /// left, the day keeps a log of 0 if it was logged as a total before the first entry, and reads as not logged otherwise.</summary>
    private async Task<FoodResult> RollUpAsync(int userId, DateOnly day, int? entryId = null, FoodInfo? food = null)
    {
        int total, count;
        bool loggedAsTotal;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var sums = await db.FoodEntries.AsNoTracking().Where(e => e.UserId == userId && e.Date == day)
                .GroupBy(e => 1).Select(g => new { Total = g.Sum(e => e.Kcal), Count = g.Count() }).FirstOrDefaultAsync();
            total = sums?.Total ?? 0;
            count = sums?.Count ?? 0;

            var log = await db.CalorieLogs.FirstOrDefaultAsync(c => c.UserId == userId && c.Date == day);
            loggedAsTotal = log?.LoggedAsTotal ?? false;
            if (count == 0 && log is not null && !loggedAsTotal)
            {
                db.CalorieLogs.Remove(log);   // nothing left to say the day was logged
                await db.SaveChangesAsync();
                return new FoodResult(true, Points: await points.SyncAsync(userId, day));
            }
            if (count == 0 && log is null) return new FoodResult(true);
        }

        var saved = await calories.LogTodayAsync(userId, total, day);
        if (!saved.Success) return FoodResult.Fail(saved.Error ?? "Could not save your calories.");

        FoodEntryInfo? info = null;
        if (entryId is { } id)
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var e = await db.FoodEntries.AsNoTracking().Include(x => x.Food).FirstOrDefaultAsync(x => x.Id == id);
            info = e is null ? null : ToEntry(e);
        }
        return new FoodResult(true, Entry: info, Food: food, Points: saved.Points);
    }

    private Task<FoodResult> FinishAsync(int userId, DateOnly day, int entryId, FoodInfo? food) => RollUpAsync(userId, day, entryId, food);

    /// <summary>A day that already has a typed total and no entries remembers that, before its first entry replaces it.</summary>
    private static async Task MarkLoggedAsTotalAsync(AppDbContext db, int userId, DateOnly day)
    {
        var log = await db.CalorieLogs.FirstOrDefaultAsync(c => c.UserId == userId && c.Date == day);
        if (log is null || log.LoggedAsTotal) return;
        if (!await db.FoodEntries.AnyAsync(e => e.UserId == userId && e.Date == day)) log.LoggedAsTotal = true;
    }

    /// <summary>An error when adding <paramref name="delta"/> kcal would push the day over the limit.</summary>
    private static async Task<string?> TotalErrorAsync(AppDbContext db, int userId, DateOnly day, int delta)
    {
        var current = await db.FoodEntries.AsNoTracking().Where(e => e.UserId == userId && e.Date == day).SumAsync(e => (int?)e.Kcal) ?? 0;
        return CalorieGoals.CheckConsumed(current + delta) is null ? null : $"A day can't go over {CalorieGoals.K(CalorieGoals.MaxConsumed)} kcal.";
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>The user's row for this food, inserting it (a copy from the catalog or Open Food Facts) the first time.</summary>
    private async Task<Food?> EnsureSavedAsync(AppDbContext db, int userId, FoodInfo info)
    {
        if (info.FoodId is { } id)
            return await db.Foods.FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId);
        if (info.Source == FoodSource.Mine && info.SourceKey is null) return null;   // a "Mine" food always has a row

        var existing = await db.Foods.FirstOrDefaultAsync(f => f.UserId == userId && f.Source == info.Source && f.SourceKey == info.SourceKey);
        if (existing is not null)
        {
            existing.IsHidden = false;
            return existing;
        }

        var row = new Food
        {
            UserId = userId, Source = info.Source, SourceKey = info.SourceKey, Name = info.Name, Brand = info.Brand,
            IsLiquid = info.IsLiquid, KcalPer100 = info.KcalPer100, ProteinPer100 = info.ProteinPer100, CarbsPer100 = info.CarbsPer100,
            FatPer100 = info.FatPer100, ServingName = info.ServingName, ServingGrams = info.ServingGrams
        };
        db.Foods.Add(row);
        return row;
    }

    private static Food? PreferSaved(List<Food> foods, string key) =>
        foods.Where(f => f.SourceKey == key).OrderBy(f => f.Source == FoodSource.Mine ? 0 : 1).FirstOrDefault();

    private static string? Check(FoodInput i) =>
        FoodRules.CheckInput(i.Name, i.Brand, i.Kcal, i.Protein, i.Carbs, i.Fat, i.ServingGrams, i.ServingName)
        ?? (i.Barcode is { Length: > 0 } b && CleanBarcode(b) is null ? "That is not a barcode number." : null)
        ?? (i.ServingName is { Length: > 0 } && i.ServingGrams is null ? "Give the serving's weight in grams." : null);

    private static string? CleanBarcode(string? barcode)
    {
        var b = (barcode ?? "").Trim();
        return b.Length == 0 ? null : OpenFoodFactsClient.IsBarcode(b) ? b : null;
    }

    private static void Apply(Food row, FoodInput i)
    {
        row.Name = i.Name.Trim();
        row.Brand = string.IsNullOrWhiteSpace(i.Brand) ? null : i.Brand.Trim();
        row.IsLiquid = i.IsLiquid;
        row.KcalPer100 = Math.Round(i.Kcal!.Value, 1);
        row.ProteinPer100 = i.Protein is { } p ? Math.Round(p, 2) : null;
        row.CarbsPer100 = i.Carbs is { } c ? Math.Round(c, 2) : null;
        row.FatPer100 = i.Fat is { } f ? Math.Round(f, 2) : null;
        row.ServingGrams = i.ServingGrams;
        row.ServingName = i.ServingGrams is null ? null : string.IsNullOrWhiteSpace(i.ServingName) ? "1 serving" : i.ServingName.Trim();
    }

    private static double? ToGrams(double amount, AmountUnit unit, double? servingGrams) =>
        unit == AmountUnit.Grams ? amount : servingGrams is > 0 ? amount * servingGrams : null;

    private static double? Macro(double? per100, double grams) => per100 is { } v ? FoodRules.Scale(v, grams) : null;
    private static double? Scaled(double? v, double ratio) => v is { } x ? Math.Round(x * ratio, 1, MidpointRounding.AwayFromZero) : null;
    private static double Sum(IEnumerable<FoodEntryInfo> entries, Func<FoodEntryInfo, double?> pick) => Math.Round(entries.Sum(e => pick(e) ?? 0), 1);
    private static string? CleanLabel(string? label) => string.IsNullOrWhiteSpace(label) ? null : label.Trim();

    private static List<string> Words(string? text) =>
        (text ?? "").Split([' ', '\t', ',', '.', '-', '(', ')'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static FoodEntryInfo ToEntry(FoodEntry e) =>
        new(e.Id, e.Date, e.Meal, e.FoodId, e.Food?.Name ?? e.Label ?? (e.Grams is null ? "Quick add" : "Deleted food"), e.Food?.Brand, e.Grams, e.Servings,
            e.Food?.ServingName, e.Food?.IsLiquid ?? false, e.Kcal, e.Protein, e.Carbs, e.Fat);

    internal static FoodInfo ToInfo(Food f) =>
        new(f.Id, f.Source, f.SourceKey, f.Name, f.Brand, f.IsLiquid, f.KcalPer100, f.ProteinPer100, f.CarbsPer100, f.FatPer100,
            f.ServingName, f.ServingGrams, f.IsFavourite,
            FoodRules.CheckPer100(f.KcalPer100, f.ProteinPer100, f.CarbsPer100, f.FatPer100) ?? FoodRules.CheckServing(f.ServingGrams));

    private static FoodInfo ToInfo(CatalogFood c)
    {
        var serving = FoodRules.CheckServing(c.ServingGrams) is null ? c.ServingGrams : null;
        return new FoodInfo(null, FoodSource.Usda, c.FdcId.ToString(), c.Name, null, false, c.Kcal, c.Protein, c.Carbs, c.Fat,
            serving is null ? null : c.ServingName, serving,
            Problem: FoodRules.CheckPer100(c.Kcal, c.Protein, c.Carbs, c.Fat));
    }
}
