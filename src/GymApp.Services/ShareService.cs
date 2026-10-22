using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Client;
using GymApp.Contracts;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

public record ShareSendResult(bool Success, string? Error = null, int Sent = 0);

/// <summary>A share waiting for the signed-in user. <see cref="FromUserId"/> is a server id.</summary>
public record ReceivedShare(long Id, int FromUserId, string FromUsername, DateTime SentAt, SharedSplit? Split, SharedWorkout? Workout);

/// <summary>Shares as the app sends and reads them. Only the social server carries them between phones.</summary>
public interface IShareService
{
    Task<ShareSendResult> SendAsync(ShareRequest request);
    /// <summary>Waiting shares, or null when the server couldn't be reached.</summary>
    Task<IReadOnlyList<ReceivedShare>?> GetIncomingAsync();
    /// <summary>Removes a share once added or dismissed; null on success, else the message.</summary>
    Task<string?> RemoveAsync(long id);
}

/// <summary>Through the social server (<c>/v1/shares</c>). Not queued or cached: offline, sending says so and the inbox
/// just shows no shares until the next load.</summary>
public class RemoteShareService(SocialApiClient api) : IShareService
{
    public async Task<ShareSendResult> SendAsync(ShareRequest request)
    {
        if (ShareRules.Validate(request) is { } error) return new(false, error);
        var r = await api.SendShareAsync(request);
        return r.IsOk ? new(true, Sent: r.Value!.Sent) : new(false, r.Error);
    }

    public async Task<IReadOnlyList<ReceivedShare>?> GetIncomingAsync()
    {
        var r = await api.GetSharesAsync();
        return r.IsOk
            ? r.Value!.Select(s => new ReceivedShare(s.Id, s.FromUserId, s.FromUsername, s.SentAt, s.Split, s.Workout)).ToList()
            : null;
    }

    public async Task<string?> RemoveAsync(long id)
    {
        var r = await api.RemoveShareAsync(id);
        return r.IsOk || r.Status == 404 ? null : r.Error;   // already gone is fine
    }
}

/// <summary>The TestBench's local mode has no server to carry shares.</summary>
public class LocalShareService : IShareService
{
    public const string NoServerMessage = "Sharing needs the social server.";

    public Task<ShareSendResult> SendAsync(ShareRequest request) => Task.FromResult(new ShareSendResult(false, NoServerMessage));
    public Task<IReadOnlyList<ReceivedShare>?> GetIncomingAsync() => Task.FromResult<IReadOnlyList<ReceivedShare>?>([]);
    public Task<string?> RemoveAsync(long id) => Task.FromResult<string?>(null);
}

/// <summary>A friend shared a whole split with every day's exercises.</summary>
public sealed record SharedSplitItem(long ShareId, int FromUserId, string FromUsername, DateTime At, SharedSplit Split)
    : InboxItem($"share:{ShareId}", InboxKind.SharedSplit, FromUserId, FromUsername, At);

/// <summary>A friend shared one workout (a split day's exercise list).</summary>
public sealed record SharedWorkoutItem(long ShareId, int FromUserId, string FromUsername, DateTime At, SharedWorkout Workout)
    : InboxItem($"share:{ShareId}", InboxKind.SharedWorkout, FromUserId, FromUsername, At);

public class ShareInboxSource(IShareService shares) : IInboxSource
{
    public async Task<InboxBatch> GetAsync(int userId)
    {
        var incoming = await shares.GetIncomingAsync();
        if (incoming is null) return new InboxBatch([], IsStale: true);
        return new InboxBatch(incoming.Select(s => s.Split is { } split
                ? (InboxItem)new SharedSplitItem(s.Id, s.FromUserId, s.FromUsername, s.SentAt, split)
                : new SharedWorkoutItem(s.Id, s.FromUserId, s.FromUsername, s.SentAt, s.Workout!))
            .ToList());
    }
}

/// <summary>A split the user can share: their custom splits, plus presets they train with or filled in.</summary>
public record ShareableSplit(int SplitId, string Name, int DayCount, int ExerciseCount, bool IsActive);

/// <summary>What accepting a shared split does to the receiver's own exercises.</summary>
public enum SplitImportMode
{
    /// <summary>Use the receiver's own copy of the preset; the shared exercises replace theirs on its days.</summary>
    ReplaceMine,
    /// <summary>Add it as a new custom split; the receiver's splits keep their exercises.</summary>
    NewCustom
}

public record ShareImportResult(bool Success, string? Error = null);

/// <summary>One training day of the week a shared split would give, Monday first.</summary>
public record ImportedDay(DayOfWeek Weekday, string Name);

/// <summary>Turns the user's splits and workouts into shares, and adds received ones to their own. Exercises travel by
/// name: built-ins match by name, custom ones match the receiver's own by name or are created.</summary>
public class SplitShareService(IDbContextFactory<AppDbContext> dbFactory, SplitService splits)
{
    public const string NoRoomMessage = "You already have 5 custom splits. Delete one on Splits first.";

    // ===== Sending =====

    public async Task<List<ShareableSplit>> GetShareableSplitsAsync(int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var activeId = await db.Users.Where(u => u.Id == userId).Select(u => u.SelectedSplitId).FirstOrDefaultAsync();
        var counts = await db.UserDayExercises.Where(x => x.UserId == userId)
            .GroupBy(x => x.SplitDay.SplitId).Select(g => new { SplitId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SplitId, x => x.Count);
        var list = await db.WorkoutSplits.AsNoTracking().Include(s => s.Days)
            .Where(s => s.OwnerUserId == userId || (s.OwnerUserId == null && (s.Id == activeId || counts.Keys.Contains(s.Id))))
            .ToListAsync();
        return list
            .Select(s => new ShareableSplit(s.Id, s.Name, s.Days.Count, counts.GetValueOrDefault(s.Id), s.Id == activeId))
            .OrderByDescending(s => s.IsActive).ThenBy(s => s.Name)
            .ToList();
    }

    /// <summary>The split with every day's exercises, or null if it isn't one of the user's. A preset carries the
    /// user's weekly layout when it's their active split, else its default one.</summary>
    public async Task<SharedSplit?> BuildSplitAsync(int userId, int splitId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var split = await db.WorkoutSplits.AsNoTracking().Include(s => s.Days).Include(s => s.Variants)
            .FirstOrDefaultAsync(s => s.Id == splitId && (s.OwnerUserId == null || s.OwnerUserId == userId));
        if (split is null) return null;

        var lists = await ListsAsync(db, userId, split.Days.Select(d => d.Id));
        var days = split.Days.OrderBy(d => d.Order)
            .Select(d => new SharedDay(d.Name, split.OwnerUserId is null ? null : d.Weekday, lists[d.Id].ToList()))
            .ToList();
        if (split.OwnerUserId is not null) return new SharedSplit(split.Name, null, null, days);

        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        var variant = (user.SelectedSplitId == split.Id ? split.Variants.FirstOrDefault(v => v.Id == user.SelectedVariantId) : null)
                      ?? split.Variants.FirstOrDefault(v => v.IsDefault) ?? split.Variants.First();
        return new SharedSplit(split.Name, split.Id, variant.Pattern, days);
    }

    public async Task<SharedWorkout?> BuildWorkoutAsync(int userId, int splitDayId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var day = await db.SplitDays.AsNoTracking().Include(d => d.Split)
            .FirstOrDefaultAsync(d => d.Id == splitDayId && (d.Split.OwnerUserId == null || d.Split.OwnerUserId == userId));
        if (day is null) return null;
        var lists = await ListsAsync(db, userId, [day.Id]);
        return new SharedWorkout(day.Name, day.Split.Name, lists[day.Id].ToList());
    }

    private static async Task<ILookup<int, SharedExercise>> ListsAsync(AppDbContext db, int userId, IEnumerable<int> dayIds)
    {
        var ids = dayIds.ToList();
        return (await db.UserDayExercises.AsNoTracking()
                .Where(x => x.UserId == userId && ids.Contains(x.SplitDayId))
                .OrderBy(x => x.Order)
                .Select(x => new { x.SplitDayId, x.Exercise.Name, x.Exercise.MuscleGroup, x.Exercise.OwnerUserId, x.WarmupSets, x.WorkingSets })
                .ToListAsync())
            .ToLookup(x => x.SplitDayId,
                x => new SharedExercise(x.Name, x.MuscleGroup.ToString(), x.OwnerUserId is not null, x.WarmupSets, x.WorkingSets));
    }

    // ===== Receiving =====

    /// <summary>Whether the user can add the split onto their own copy (only presets: everyone has them).</summary>
    public static bool CanReplaceMine(SharedSplit split) => split.PresetId is not null;

    /// <summary>The training week the split gives when day 1 falls on <paramref name="start"/>. A preset follows its
    /// layout; a custom split keeps its gaps, shifted so its first day lands on <paramref name="start"/>.</summary>
    public static IReadOnlyList<(ImportedDay Day, SharedDay From)> Week(SharedSplit split, DayOfWeek start)
    {
        if (split.Pattern is { } pattern)
            return WeekSchedule.Build(pattern, start, split.Days.Count)
                .Where(s => s.IsTraining)
                .Select(s => (new ImportedDay(s.Weekday, split.Days[s.RotationIndex!.Value].Name), split.Days[s.RotationIndex!.Value]))
                .ToList();

        // Monday = 0 … Sunday = 6, so shifting wraps around the week
        static int MondayFirst(DayOfWeek d) => ((int)d + 6) % 7;
        static DayOfWeek FromMondayFirst(int i) => (DayOfWeek)((i + 1) % 7);
        var ordered = split.Days.OrderBy(d => MondayFirst(d.Weekday!.Value)).ToList();
        var shift = MondayFirst(start) - MondayFirst(ordered[0].Weekday!.Value);
        return ordered
            .Select(d => (Day: new ImportedDay(FromMondayFirst((MondayFirst(d.Weekday!.Value) + shift + 7) % 7), d.Name), From: d))
            .OrderBy(x => MondayFirst(x.Day.Weekday))
            .ToList();
    }

    /// <summary>Days of a rotating preset that a fixed-weekday copy can't hold (more days than training slots).</summary>
    public static int DaysLeftOut(SharedSplit split, DayOfWeek start) =>
        split.Days.Count - Week(split, start).Select(w => w.From).Distinct().Count();

    /// <summary>Adds the split and makes it the active one, with day 1 on <paramref name="start"/>.</summary>
    public async Task<ShareImportResult> ImportSplitAsync(int userId, SharedSplit split, DayOfWeek start, SplitImportMode mode)
    {
        if (mode == SplitImportMode.ReplaceMine && !CanReplaceMine(split)) mode = SplitImportMode.NewCustom;
        await using var db = await dbFactory.CreateDbContextAsync();

        if (mode == SplitImportMode.ReplaceMine)
        {
            var preset = await db.WorkoutSplits.AsNoTracking().Include(s => s.Days).Include(s => s.Variants)
                .FirstOrDefaultAsync(s => s.Id == split.PresetId && s.OwnerUserId == null);
            if (preset is null) return new(false, "That split isn't in this version of the app. Update GainStreak.");
            var days = preset.Days.OrderBy(d => d.Order).ToList();
            if (days.Count != split.Days.Count) return new(false, "That split doesn't match yours. Add it as a new split instead.");

            var resolved = await ResolveAsync(db, userId, split.Days.SelectMany(d => d.Exercises));
            if (resolved.Error is { } error) return new(false, error);
            for (var i = 0; i < days.Count; i++)
                await WriteDayAsync(db, userId, days[i].Id, split.Days[i].Exercises, resolved.Ids, replace: true);

            var variant = preset.Variants.FirstOrDefault(v => v.Pattern == split.Pattern)
                          ?? preset.Variants.FirstOrDefault(v => v.IsDefault) ?? preset.Variants.First();
            return await splits.SelectAsync(userId, preset.Id, variant.Id, start)
                ? new(true) : new(false, "Could not switch to that split.");
        }

        if (await db.WorkoutSplits.CountAsync(s => s.OwnerUserId == userId) >= SplitService.MaxCustomSplits)
            return new(false, NoRoomMessage);
        var week = Week(split, start);
        var name = await FreeNameAsync(db, userId, split.Name.Trim());
        var created = await splits.CreateAsync(userId, name, week.Select(w => new DayInput(w.Day.Weekday, w.Day.Name)).ToList());
        if (!created.Success) return new(false, created.Error);

        var newSplit = await db.WorkoutSplits.AsNoTracking().Include(s => s.Days)
            .FirstAsync(s => s.OwnerUserId == userId && s.Name == name);
        var ids = await ResolveAsync(db, userId, split.Days.SelectMany(d => d.Exercises));
        if (ids.Error is { } err) return new(false, err);
        foreach (var (day, from) in week)
            if (newSplit.Days.FirstOrDefault(d => d.Weekday == day.Weekday) is { } target)
                await WriteDayAsync(db, userId, target.Id, from.Exercises, ids.Ids, replace: true);

        return await splits.SelectAsync(userId, newSplit.Id, null, null)
            ? new(true) : new(false, "The split was added, but could not be made active. Pick it on Splits.");
    }

    /// <summary>Loads a shared workout into a day of the user's active split: added to the end (skipping exercises
    /// already there) or replacing the list.</summary>
    public async Task<ShareImportResult> ImportWorkoutAsync(int userId, SharedWorkout workout, int splitDayId, CopyMode mode)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var activeId = await db.Users.Where(u => u.Id == userId).Select(u => u.SelectedSplitId).FirstOrDefaultAsync();
        if (!await db.SplitDays.AnyAsync(d => d.Id == splitDayId && d.SplitId == activeId))
            return new(false, "Pick a day of your current split.");

        var resolved = await ResolveAsync(db, userId, workout.Exercises);
        if (resolved.Error is { } error) return new(false, error);
        return await WriteDayAsync(db, userId, splitDayId, workout.Exercises, resolved.Ids, mode == CopyMode.Replace)
            is { } problem ? new(false, problem) : new(true);
    }

    /// <summary>Exercise ids by lowercased name, creating the custom ones the user doesn't have. A built-in the app
    /// doesn't know (renamed in another version) becomes a custom exercise too.</summary>
    private static async Task<(Dictionary<string, int> Ids, string? Error)> ResolveAsync(
        AppDbContext db, int userId, IEnumerable<SharedExercise> exercises)
    {
        var wanted = exercises.GroupBy(e => e.Name.Trim().ToLowerInvariant()).Select(g => g.First()).ToList();
        var known = await db.Exercises.AsNoTracking()
            .Where(e => e.OwnerUserId == null || e.OwnerUserId == userId)
            .Select(e => new { e.Id, e.Name, e.OwnerUserId })
            .ToListAsync();
        var ids = new Dictionary<string, int>();
        var missing = new List<SharedExercise>();
        foreach (var e in wanted)
        {
            var key = e.Name.Trim().ToLowerInvariant();
            // Prefer a built-in for a built-in, the user's own for a custom one
            var hit = known.Where(k => k.Name.ToLowerInvariant() == key)
                .OrderBy(k => (k.OwnerUserId is null) == !e.IsCustom ? 0 : 1).FirstOrDefault();
            if (hit is not null) ids[key] = hit.Id;
            else missing.Add(e);
        }
        if (missing.Count == 0) return (ids, null);

        var custom = known.Count(k => k.OwnerUserId == userId);
        if (custom + missing.Count > ExerciseService.MaxCustomExercises)
            return (ids, $"This needs {missing.Count} new custom exercises, more than your {ExerciseService.MaxCustomExercises} allow. Delete some first.");
        foreach (var e in missing)
        {
            var group = Enum.TryParse<MuscleGroup>(e.MuscleGroup, out var g) ? g : MuscleGroup.Core;
            var row = new Exercise { Name = e.Name.Trim(), MuscleGroup = group, OwnerUserId = userId };
            db.Exercises.Add(row);
            await db.SaveChangesAsync();
            ids[e.Name.Trim().ToLowerInvariant()] = row.Id;
        }
        return (ids, null);
    }

    /// <summary>Writes a day's list; null when done, else why not. Sets are kept within the app's limits.</summary>
    private static async Task<string?> WriteDayAsync(AppDbContext db, int userId, int splitDayId,
        IReadOnlyList<SharedExercise> exercises, Dictionary<string, int> ids, bool replace)
    {
        var existing = await db.UserDayExercises.Where(x => x.UserId == userId && x.SplitDayId == splitDayId)
            .OrderBy(x => x.Order).ToListAsync();
        var keep = replace ? [] : existing;
        var have = keep.Select(x => x.ExerciseId).ToHashSet();
        var toAdd = exercises.Select(e => (Id: ids[e.Name.Trim().ToLowerInvariant()], e))
            .Where(x => have.Add(x.Id)).ToList();
        if (keep.Count + toAdd.Count > DayExerciseService.MaxExercisesPerDay)
            return $"That would make more than {DayExerciseService.MaxExercisesPerDay} exercises on that day. Replace the list instead.";

        if (replace) db.UserDayExercises.RemoveRange(existing);
        var order = keep.Count == 0 ? 0 : keep.Max(x => x.Order);
        foreach (var (id, e) in toAdd)
            db.UserDayExercises.Add(new UserDayExercise
            {
                UserId = userId, SplitDayId = splitDayId, ExerciseId = id, Order = ++order,
                WarmupSets = Math.Clamp(e.WarmupSets, 0, DayExerciseService.MaxWarmupSets),
                WorkingSets = Math.Clamp(e.WorkingSets, 1, DayExerciseService.MaxWorkingSets)
            });
        await db.SaveChangesAsync();
        return null;
    }

    /// <summary>"PPL", else "PPL (2)", "PPL (3)"…, within the 40-character limit.</summary>
    private static async Task<string> FreeNameAsync(AppDbContext db, int userId, string name)
    {
        var taken = (await db.WorkoutSplits.Where(s => s.OwnerUserId == userId).Select(s => s.Name).ToListAsync())
            .Select(n => n.ToLowerInvariant()).ToHashSet();
        if (!taken.Contains(name.ToLowerInvariant())) return name;
        for (var i = 2; ; i++)
        {
            var suffix = $" ({i})";
            var candidate = name[..Math.Min(name.Length, ShareRules.MaxSplitName - suffix.Length)] + suffix;
            if (!taken.Contains(candidate.ToLowerInvariant())) return candidate;
        }
    }
}
