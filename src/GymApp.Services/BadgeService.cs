using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;
using CorePrAdjustment = GymApp.Core.PrAdjustment;

namespace GymApp.Services;

/// <summary>A badge reached, or a tier climbed, by a <see cref="BadgeService.CheckAsync"/> (not by the quiet backfill).</summary>
public sealed record BadgeUnlock(string BadgeId, int Tier, DateOnly EarnedOn);

/// <summary>The badge settings (Phase 25). <paramref name="FeaturedId"/> is what to show next to the name, or null;
/// <paramref name="IsAutomatic"/> when the user hasn't picked one (or their pick is no longer earned).</summary>
public sealed record BadgeSettings(string? FeaturedId, int FeaturedTier, bool IsAutomatic, bool ShowsNone, bool Share, bool Imperial);

/// <summary>What one check did: the new unlocks, newest first, and whether this was the first check (the backfill).</summary>
public sealed record BadgeCheck(IReadOnlyList<BadgeUnlock> Unlocks, bool Backfilled);

/// <summary>
/// Badges on this phone (Phase 25): builds <see cref="BadgeInputs"/> from the database, runs
/// <see cref="BadgeEvaluator"/> and stores what was earned. Stored tiers only ever go up, so a badge is kept even when
/// the logs behind it are edited or deleted. The first check stores the whole history quietly (already seen) and
/// flags the one "from your history" sheet; later checks return what is new for the unlock sheet.
/// </summary>
public class BadgeService(IDbContextFactory<AppDbContext> dbFactory)
{
    public const string WeGoJimEvent = "we-go-jim";
    public const string WeekResultEvent = "top-of-the-board";
    public const string InvitesEvent = "recruiter";

    private static readonly MuscleGroup[] Legs = [MuscleGroup.Quads, MuscleGroup.Hamstrings, MuscleGroup.Glutes, MuscleGroup.Calves];

    /// <summary>
    /// Recomputes the badges and stores what was earned. Call it after anything is logged (the sync hooks), on
    /// foreground and on screen changes. Pass <paramref name="friendCheckIns"/> when they were just fetched: days the
    /// user trained at the same gym as a friend are stored, since the server keeps check-ins only 31 days.
    /// </summary>
    public async Task<BadgeCheck> CheckAsync(int userId, DateOnly? today = null, IReadOnlyList<FriendCheckIn>? friendCheckIns = null)
    {
        var day = today ?? AppClock.Today;
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return new BadgeCheck([], false);

        if (friendCheckIns is not null)
        {
            await StoreFriendDaysAsync(db, userId, friendCheckIns, day);
            await db.SaveChangesAsync();                                  // the inputs below read them back
        }

        var stored = await db.EarnedBadges.Where(e => e.UserId == userId).ToDictionaryAsync(e => e.BadgeId);
        var inputs = await BuildInputsAsync(db, user, day, stored.Keys.ToHashSet());
        var progress = BadgeEvaluator.Evaluate(inputs, day);

        var backfill = !user.BadgesBackfilled;
        var now = AppClock.Now;
        var unlocks = new List<BadgeUnlock>();
        foreach (var p in progress.Where(p => p.Earned))
        {
            var earnedOn = p.EarnedOn ?? day;
            if (!stored.TryGetValue(p.Id, out var row))
            {
                db.EarnedBadges.Add(new EarnedBadge
                    { UserId = userId, BadgeId = p.Id, Tier = p.Tier, EarnedOn = earnedOn, SeenAt = backfill ? now : null });
                unlocks.Add(new BadgeUnlock(p.Id, p.Tier, earnedOn));
            }
            else if (p.Tier > row.Tier)                                      // raise only: never taken away
            {
                row.Tier = p.Tier;
                row.EarnedOn = earnedOn;
                row.SeenAt = backfill ? now : null;
                unlocks.Add(new BadgeUnlock(p.Id, p.Tier, earnedOn));
            }
        }

        if (backfill)
        {
            user.BadgesBackfilled = true;
            user.BadgeHistorySheetPending = unlocks.Count > 0;
        }
        await db.SaveChangesAsync();
        return backfill
            ? new BadgeCheck([], true)
            : new BadgeCheck(unlocks.OrderByDescending(u => u.EarnedOn).ToList(), false);
    }

    /// <summary>What the badge screen shows: every badge's progress, with the stored (kept) tiers on top. Nothing is
    /// stored; call <see cref="CheckAsync"/> for that.</summary>
    public async Task<IReadOnlyList<BadgeProgress>> GetProgressAsync(int userId, DateOnly? today = null)
    {
        var day = today ?? AppClock.Today;
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return [];
        var stored = await db.EarnedBadges.AsNoTracking().Where(e => e.UserId == userId).ToDictionaryAsync(e => e.BadgeId);
        var progress = BadgeEvaluator.Evaluate(await BuildInputsAsync(db, user, day, stored.Keys.ToHashSet()), day);
        return progress.Select(p => stored.TryGetValue(p.Id, out var row) && (!p.Earned || row.Tier > p.Tier)
                ? Kept(p, row)
                : p)
            .ToList();
    }

    /// <summary>A badge whose stored tier is above what the logs show now (they were edited or deleted): the stored
    /// tier stays, and the bar aims at the tier after it.</summary>
    private static BadgeProgress Kept(BadgeProgress p, EarnedBadge row)
    {
        var tiers = BadgeCatalog.Find(p.Id)?.Tiers ?? [];
        var tier = Math.Max(p.Tier, row.Tier);
        double? target = tiers.Count == 0 ? null : tier < tiers.Count ? tiers[tier] : null;
        return p with { Earned = true, Tier = tier, EarnedOn = row.EarnedOn, Target = target };
    }

    /// <summary>The user's earned badges.</summary>
    public async Task<IReadOnlyList<EarnedBadge>> GetEarnedAsync(int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.EarnedBadges.AsNoTracking().Where(e => e.UserId == userId)
            .OrderByDescending(e => e.EarnedOn).ThenBy(e => e.BadgeId).ToListAsync();
    }

    /// <summary>Unlocks not shown yet (newest first), e.g. when the app closed before the sheet came up.</summary>
    public async Task<IReadOnlyList<BadgeUnlock>> GetUnseenAsync(int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return (await db.EarnedBadges.AsNoTracking().Where(e => e.UserId == userId && e.SeenAt == null).ToListAsync())
            .OrderByDescending(e => e.EarnedOn)
            .Select(e => new BadgeUnlock(e.BadgeId, e.Tier, e.EarnedOn))
            .ToList();
    }

    /// <summary>The unlock sheet showed these.</summary>
    public async Task MarkSeenAsync(int userId, IEnumerable<string> badgeIds)
    {
        var ids = badgeIds.ToList();
        await using var db = await dbFactory.CreateDbContextAsync();
        var now = AppClock.Now;
        foreach (var row in await db.EarnedBadges.Where(e => e.UserId == userId && ids.Contains(e.BadgeId) && e.SeenAt == null).ToListAsync())
            row.SeenAt = now;
        await db.SaveChangesAsync();
    }

    /// <summary>How many badges the history earned, once: the "from your history" sheet shows it, then it's cleared.
    /// Null when there is no sheet to show.</summary>
    public async Task<int?> TakeHistorySheetAsync(int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is not { BadgeHistorySheetPending: true }) return null;
        user.BadgeHistorySheetPending = false;
        await db.SaveChangesAsync();
        return await db.EarnedBadges.CountAsync(e => e.UserId == userId);
    }

    /// <summary>"none" in <see cref="UserProfile.FeaturedBadge"/>: the user chose to show no badge.</summary>
    public const string NoFeatured = "none";

    /// <summary>The user's badge settings and the featured badge to show: the chosen one while it's still earned,
    /// else the automatic one (the highest tier, then the newest), or none.</summary>
    public async Task<BadgeSettings> GetSettingsAsync(int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var profile = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);
        var earned = await db.EarnedBadges.AsNoTracking().Where(e => e.UserId == userId).ToListAsync();
        var choice = profile?.FeaturedBadge;
        var featured = Resolve(choice, earned);
        return new BadgeSettings(featured?.BadgeId, featured?.Tier ?? 0, choice is null || (choice != NoFeatured && featured?.BadgeId != choice),
            choice == NoFeatured, profile?.ShareBadges ?? true, profile?.Units == UnitSystem.Imperial);
    }

    /// <summary>The featured badge for a stored choice: none for <see cref="NoFeatured"/>, the chosen badge while it's
    /// still earned, else the automatic one.</summary>
    public static EarnedBadge? Resolve(string? choice, IEnumerable<EarnedBadge> earned)
    {
        if (choice == NoFeatured) return null;
        var list = earned.ToList();
        return list.FirstOrDefault(e => e.BadgeId == choice) ?? Automatic(list);
    }

    /// <summary>The automatic featured badge: the highest tier (one-time badges count as below Steel), then the newest.</summary>
    public static EarnedBadge? Automatic(IEnumerable<EarnedBadge> earned) =>
        earned.OrderByDescending(e => e.Tier).ThenByDescending(e => e.EarnedOn).ThenBy(e => e.BadgeId).FirstOrDefault();

    /// <summary>Sets the featured badge: an earned badge's id, <see cref="NoFeatured"/>, or null for automatic.</summary>
    public async Task SetFeaturedAsync(int userId, string? badgeId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var profile = await ProfileAsync(db, userId);
        profile.FeaturedBadge = badgeId;
        profile.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>Whether friends may see the user's badges.</summary>
    public async Task SetShareAsync(int userId, bool share)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var profile = await ProfileAsync(db, userId);
        profile.ShareBadges = share;
        profile.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>Clears every badge and the backfill flag, for the TestBench's admin window only.</summary>
    public async Task ResetAsync(int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.EarnedBadges.Where(e => e.UserId == userId).ExecuteDeleteAsync();
        await db.BadgeEvents.Where(e => e.UserId == userId).ExecuteDeleteAsync();
        var user = await db.Users.FirstAsync(u => u.Id == userId);
        user.BadgesBackfilled = false;
        user.BadgeHistorySheetPending = false;
        await db.SaveChangesAsync();
    }

    /// <summary>Stores a badge as just earned at <paramref name="tier"/>, unseen, for the TestBench's admin window.</summary>
    public async Task AwardAsync(int userId, string badgeId, int tier, DateOnly? on = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.EarnedBadges.FirstOrDefaultAsync(e => e.UserId == userId && e.BadgeId == badgeId);
        if (row is null) db.EarnedBadges.Add(row = new EarnedBadge { UserId = userId, BadgeId = badgeId });
        row.Tier = tier;
        row.EarnedOn = on ?? AppClock.Today;
        row.SeenAt = null;
        var user = await db.Users.FirstAsync(u => u.Id == userId);
        user.BadgesBackfilled = true;
        await db.SaveChangesAsync();
    }

    private static async Task<UserProfile> ProfileAsync(AppDbContext db, int userId)
    {
        var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        if (profile is null) db.UserProfiles.Add(profile = new UserProfile { UserId = userId });
        return profile;
    }

    /// <summary>A finished leaderboard week (Top of the Board, 25d). Stored once per week; a later call for the same
    /// week changes nothing, so a week is never counted twice.</summary>
    public async Task RecordWeekResultAsync(int userId, DateOnly weekStart, bool first)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.BadgeEvents.AnyAsync(e => e.UserId == userId && e.Kind == WeekResultEvent && e.Date == weekStart)) return;
        db.BadgeEvents.Add(new BadgeEvent { UserId = userId, Kind = WeekResultEvent, Date = weekStart, Value = first ? 1 : 0 });
        await db.SaveChangesAsync();
    }

    /// <summary>How many people joined through the user's invite (Recruiter), from the server. Kept as the highest
    /// count seen, so a friend who leaves doesn't take the count down.</summary>
    public async Task RecordAcceptedInvitesAsync(int userId, int count, DateOnly? today = null)
    {
        if (count <= 0) return;
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.BadgeEvents.FirstOrDefaultAsync(e => e.UserId == userId && e.Kind == InvitesEvent);
        if (row is null) db.BadgeEvents.Add(new BadgeEvent { UserId = userId, Kind = InvitesEvent, Date = today ?? AppClock.Today, Value = count });
        else if (count > row.Value) row.Value = count;
        else return;
        await db.SaveChangesAsync();
    }

    /// <summary>Whether the result of the week starting <paramref name="weekStart"/> is stored yet.</summary>
    public async Task<bool> HasWeekResultAsync(int userId, DateOnly weekStart)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.BadgeEvents.AnyAsync(e => e.UserId == userId && e.Kind == WeekResultEvent && e.Date == weekStart);
    }

    /// <summary>Stores the days a friend checked in at the OSM gym the user trained at that day. Friends' check-ins
    /// carry this phone's gym id when the gym is known here (<see cref="RemoteFriendCheckInSource"/>).</summary>
    private static async Task StoreFriendDaysAsync(AppDbContext db, int userId, IReadOnlyList<FriendCheckIn> checkIns, DateOnly today)
    {
        var friendDays = checkIns.Where(c => !c.IsCustomGym && c.GymId > 0 && c.Date <= today)
            .Select(c => (c.Date, c.GymId)).ToHashSet();
        if (friendDays.Count == 0) return;
        var from = friendDays.Min(f => f.Date);
        var mine = await db.WorkoutLogs.AsNoTracking()
            .Where(w => w.UserId == userId && w.Date >= from && w.GymId != null && w.Gym!.OsmId != null)
            .Select(w => new { w.Date, GymId = w.GymId!.Value })
            .ToListAsync();
        var days = mine.Where(w => friendDays.Contains((w.Date, w.GymId))).Select(w => w.Date).Distinct().ToList();
        if (days.Count == 0) return;
        var known = await db.BadgeEvents.Where(e => e.UserId == userId && e.Kind == WeGoJimEvent && days.Contains(e.Date))
            .Select(e => e.Date).ToListAsync();
        foreach (var d in days.Except(known))
            db.BadgeEvents.Add(new BadgeEvent { UserId = userId, Kind = WeGoJimEvent, Date = d, Value = 1 });
    }

    /// <summary>Everything the evaluator needs, read from this phone's database.</summary>
    internal static async Task<BadgeInputs> BuildInputsAsync(AppDbContext db, User user, DateOnly today, IReadOnlySet<string> earned)
    {
        var userId = user.Id;

        var logs = await db.WorkoutLogs.AsNoTracking().Where(w => w.UserId == userId && w.Date <= today)
            .Select(w => new
            {
                w.Date, w.GymId, GymOsmType = w.Gym == null ? null : w.Gym.OsmType, GymOsmId = w.Gym == null ? null : w.Gym.OsmId,
                SplitId = w.SplitDay == null ? (int?)null : w.SplitDay.SplitId, w.SplitDayId,
                w.FilledExercises, w.TotalExercises, w.CardioMinutes,
            })
            .ToListAsync();
        string? GymKey(int? gymId, OsmElementType? type, long? osmId) =>
            gymId is null ? null
            : type is { } t && osmId is { } id ? BadgeInputs.OsmGymKey(t.ToString(), id)
            : BadgeInputs.CustomGymKey(gymId.Value);
        var workouts = logs.Select(w => new BadgeWorkout(w.Date, GymKey(w.GymId, w.GymOsmType, w.GymOsmId), w.SplitId, w.SplitDayId,
            w.FilledExercises, w.TotalExercises, w.CardioMinutes)).ToList();

        var sets = await db.SetLogs.AsNoTracking().Where(s => s.UserId == userId && s.Date <= today)
            .Select(s => new { s.Date, s.ExerciseId, s.SetNumber, s.Kind, s.Reps, s.WeightKg, s.Exercise.MuscleGroup })
            .ToListAsync();
        var adjustments = (await db.PrAdjustments.AsNoTracking().Where(a => a.UserId == userId).ToListAsync())
            .GroupBy(a => a.ExerciseId)
            .ToDictionary(g => g.Key,
                g => (IReadOnlyList<CorePrAdjustment>)g.Select(a => new CorePrAdjustment(a.Date, a.WeightKg, a.Reps, a.OneRepMaxKg)).ToList());

        var (dates, schedule, choices, rule) = await StreakService.LoadAsync(db, userId, today);
        var streak = StreakCalculator.Calculate(dates, schedule, choices, today, rule);
        var trainingDays = dates.Count == 0
            ? []
            : StreakCalculator.Timeline(dates, schedule, choices, today, dates.Min(), today, rule);

        var splitIds = workouts.Where(w => w.SplitId is not null).Select(w => w.SplitId!.Value).Distinct().ToList();
        var splitDays = (await db.SplitDays.AsNoTracking().Where(d => splitIds.Contains(d.SplitId))
                .Select(d => new { d.SplitId, d.Id }).ToListAsync())
            .GroupBy(d => d.SplitId)
            .ToDictionary(g => g.Key, g => (IReadOnlySet<int>)g.Select(d => d.Id).ToHashSet());

        var events = await db.BadgeEvents.AsNoTracking().Where(e => e.UserId == userId).ToListAsync();
        var gymByDate = workouts.Where(w => w.GymKey is not null).ToDictionary(w => w.Date, w => w.GymKey!);
        var buddyDays = events.Where(e => e.Kind == WeGoJimEvent && gymByDate.ContainsKey(e.Date))
            .Select(e => new BadgeFriendCheckIn(e.Date, gymByDate[e.Date])).ToList();

        return new BadgeInputs
        {
            Workouts = workouts,
            Sets = sets.Select(s => new BadgeSet(s.Date, s.ExerciseId, s.SetNumber, s.Kind, s.Reps, s.WeightKg,
                Legs.Contains(s.MuscleGroup))).ToList(),
            PrAdjustments = adjustments,
            LongestStreak = Math.Max(user.LongestStreak, streak.Longest),
            TrainingDays = trainingDays,
            ColdDays = dates.Count == 0 ? [] : StreakCalculator.ColdDays(dates, schedule, choices, today, rule),
            Calories = await db.CalorieLogs.AsNoTracking().Where(c => c.UserId == userId)
                .Select(c => new BadgeCalorieDay(c.Date, c.GoalAtTime, c.CaloriesConsumed)).ToListAsync(),
            WeighIns = await db.WeightLogs.AsNoTracking().Where(w => w.UserId == userId)
                .Select(w => new BadgeWeighIn(w.Date, w.WeightKg)).ToListAsync(),
            LiveWorkouts = await db.LiveWorkouts.AsNoTracking().Where(l => l.UserId == userId)
                .Select(l => new BadgeLiveWorkout(l.Date, l.StartedAt, l.EndedAt, l.AutoStop != null)).ToListAsync(),
            // A session's minutes are already on the day's log (CardioService), so only the session itself is passed
            Cardio = await db.CardioSessions.AsNoTracking().Where(c => c.UserId == userId && c.EndedAt != null && c.SavedMinutes > 0)
                .Select(c => new BadgeCardio(c.Date, 0)).ToListAsync(),
            FriendCheckIns = buddyDays,
            SplitDays = splitDays,
            Vacations = (await StreakService.LoadVacationsAsync(db, userId))
                .Select(v => new BadgeVacation(v.Start, v.EndedEarlyOn is { } ended ? ended.AddDays(1) : v.Return)).ToList(),
            BirthDate = await db.UserProfiles.AsNoTracking().Where(p => p.UserId == userId).Select(p => p.BirthDate).FirstOrDefaultAsync(),
            WeekResults = events.Where(e => e.Kind == WeekResultEvent)
                .Select(e => new BadgeWeekResult(e.Date, e.Value == 1)).ToList(),
            AcceptedInvites = events.Where(e => e.Kind == InvitesEvent).Select(e => e.Value).DefaultIfEmpty(0).Max(),
            PreviouslyEarned = earned,
        };
    }
}
