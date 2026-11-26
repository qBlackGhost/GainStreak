using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using GymApp.Services;
using GymApp.Social;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GymApp.TestBench;

/// <summary>Fills the TestBench's local data for the presentation: a main account with five weeks of workouts, weigh-ins, food and a
/// calorie goal, and four friends with their own history (one more request waits in the Inbox). Today's workout is left open on
/// purpose, so it can be logged live. Local accounts only; nothing goes to the server.</summary>
public static class DemoData
{
    public const string Password = "demo1234";
    public const string MainUser = "alex";

    private sealed record Person(string Username, int Weeks, int SkipEvery, double Strength, double WeightKg, Sex Sex, int Age, double HeightCm);

    // Weeks of history and how often a planned day is missed (0 = never), so the leaderboard and streaks differ
    private static readonly Person[] People =
    [
        new("alex", 5, 0, 1.00, 78.0, Sex.Male, 22, 180),
        new("maya", 5, 0, 0.80, 61.0, Sex.Female, 21, 167),
        new("jordan", 5, 6, 1.15, 86.0, Sex.Male, 24, 183),
        new("sam", 4, 4, 0.90, 72.0, Sex.Male, 23, 176),
        new("riley", 3, 0, 0.70, 58.0, Sex.Female, 20, 165),
    ];

    private static readonly (DayOfWeek Day, string Name, MuscleGroup[] Groups)[] Plan =
    [
        (DayOfWeek.Monday, "Push", [MuscleGroup.Chest, MuscleGroup.Chest, MuscleGroup.Shoulders, MuscleGroup.Triceps]),
        (DayOfWeek.Tuesday, "Pull", [MuscleGroup.Back, MuscleGroup.Back, MuscleGroup.Back, MuscleGroup.Biceps]),
        (DayOfWeek.Thursday, "Legs", [MuscleGroup.Quads, MuscleGroup.Quads, MuscleGroup.Hamstrings, MuscleGroup.Calves]),
        (DayOfWeek.Friday, "Upper", [MuscleGroup.Chest, MuscleGroup.Back, MuscleGroup.Shoulders, MuscleGroup.Biceps]),
    ];

    private static readonly Dictionary<MuscleGroup, double> BaseKg = new()
    {
        [MuscleGroup.Chest] = 60, [MuscleGroup.Back] = 55, [MuscleGroup.Shoulders] = 30, [MuscleGroup.Biceps] = 16,
        [MuscleGroup.Triceps] = 25, [MuscleGroup.Quads] = 85, [MuscleGroup.Hamstrings] = 60, [MuscleGroup.Calves] = 70,
    };

    public static async Task<string> LoadAsync(IServiceProvider sp)
    {
        var dbFactory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var accounts = sp.GetRequiredService<IAccountService>();
        var friends = sp.GetRequiredService<IFriendService>();
        var problems = new List<string>();

        // 1. Accounts
        var ids = new Dictionary<string, int>();
        foreach (var p in People)
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var existing = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == p.Username);
            if (existing is null)
            {
                var r = await accounts.RegisterAsync(p.Username, $"{p.Username}@demo.test", Password);
                if (!r.Success) { problems.Add($"{p.Username}: {r.Error}"); continue; }
                existing = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == p.Username);
            }
            if (existing is not null) ids[p.Username] = existing.Id;
        }
        if (!ids.ContainsKey(MainUser)) return $"Could not create the demo account. {string.Join("; ", problems)}";

        // 2. Each person: body data, a split with exercises, five weeks of workouts, weigh-ins
        foreach (var p in People)
            if (ids.TryGetValue(p.Username, out var id))
                await SeedPersonAsync(sp, dbFactory, id, p, problems);

        // 3. The main account's calories and food
        await SeedCaloriesAsync(sp, ids[MainUser], problems);

        // 4. Friends: alex is friends with maya, jordan and sam; riley's request waits in alex's Inbox
        foreach (var other in new[] { "maya", "jordan", "sam" })
        {
            if (!ids.TryGetValue(other, out var oid)) continue;
            await friends.SendRequestAsync(ids[MainUser], oid);
            await friends.AcceptAsync(oid, ids[MainUser]);
        }
        if (ids.TryGetValue("riley", out var riley)) await friends.SendRequestAsync(riley, ids[MainUser]);

        // 5. Streaks and points for everyone
        var streaks = sp.GetRequiredService<StreakService>();
        var points = sp.GetRequiredService<PointsService>();
        foreach (var id in ids.Values)
        {
            await streaks.RecalculateAsync(id, AppClock.Today);
            await points.SyncAsync(id, AppClock.Today);
        }

        // 6. Everyone lives in Timișoara; the demo people share their gym check-ins, so alex sees his friends on the map
        await SeedMapAsync(dbFactory, ids, problems);

        // 7. Badges, already seen (no unlock sheets on the first start), so Profile and the friends' rows have some to show
        await SeedBadgesAsync(sp, ids);

        var tail = problems.Count == 0 ? "" :$" ({problems.Count} problem(s): {string.Join("; ", problems.Take(3))})";
        return $"Demo data loaded. Sign in as {MainUser} (password {Password}). Today's workout is open to log live.{tail}";
    }

    private static async Task SeedPersonAsync(IServiceProvider sp, IDbContextFactory<AppDbContext> dbFactory, int userId, Person p, List<string> problems)
    {
        var profiles = sp.GetRequiredService<ProfileService>();
        var splits = sp.GetRequiredService<SplitService>();
        var dayExercises = sp.GetRequiredService<DayExerciseService>();
        var sets = sp.GetRequiredService<SetService>();
        var today = AppClock.Today;

        await profiles.SaveAsync(userId, new ProfileInput(p.Sex, today.AddYears(-p.Age).AddDays(-40), p.HeightCm, ActivityLevel.Active, UnitSystem.Metric));

        // The split and its exercises (skipped on a second load)
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.WorkoutLogs.AnyAsync(w => w.UserId == userId)) return;

        var made = await splits.CreateAsync(userId, "Push / Pull / Legs / Upper", Plan.Select(d => new DayInput(d.Day, d.Name)).ToList());
        if (!made.Success) { problems.Add($"{p.Username} split: {made.Error}"); return; }
        var split = (await splits.GetCustomAsync(userId)).First(s => s.Name == "Push / Pull / Legs / Upper");
        await splits.SelectAsync(userId, split.Id, null, null);

        var catalog = await db.Exercises.AsNoTracking().Where(e => e.OwnerUserId == null).OrderBy(e => e.Id).ToListAsync();
        var perDay = new Dictionary<DayOfWeek, List<(int ExerciseId, MuscleGroup Group)>>();
        foreach (var plan in Plan)
        {
            var day = split.Days.First(d => d.Weekday == plan.Day);
            var picked = new List<(int, MuscleGroup)>();
            var used = new HashSet<int>();
            foreach (var group in plan.Groups)
            {
                var ex = catalog.FirstOrDefault(e => e.MuscleGroup == group && !used.Contains(e.Id));
                if (ex is null) continue;
                used.Add(ex.Id);
                var r = await dayExercises.AddAsync(userId, day.Id, ex.Id, warmupSets: 1, workingSets: 3);
                if (r.Success) picked.Add((ex.Id, group));
            }
            perDay[plan.Day] = picked;
        }

        // Workouts on the planned weekdays for the past weeks (today stays open)
        var start = today.AddDays(-7 * p.Weeks);
        var planned = 0;
        for (var d = start; d < today; d = d.AddDays(1))
        {
            if (Plan.All(x => x.Day != d.DayOfWeek)) continue;
            planned++;
            if (p.SkipEvery > 0 && planned % p.SkipEvery == 0) continue;
            var day = split.Days.First(x => x.Weekday == d.DayOfWeek);
            await using (var write = await dbFactory.CreateDbContextAsync())
            {
                write.WorkoutLogs.Add(new WorkoutLog { UserId = userId, Date = d, SplitDayId = day.Id });
                await write.SaveChangesAsync();
            }
            var week = (d.DayNumber - start.DayNumber) / 7;
            foreach (var (exId, group) in perDay[d.DayOfWeek])
            {
                var kg = Math.Round(BaseKg[group] * p.Strength * (1 + 0.025 * week) / 2.5) * 2.5;
                var rows = new List<SetInput> { new(SetKind.Warmup, 10, Math.Round(kg * 0.5 / 2.5) * 2.5) };
                rows.AddRange([new(SetKind.Working, 10, kg), new(SetKind.Working, 9, kg), new(SetKind.Working, 8, kg)]);
                var r = await sets.SaveDayAsync(userId, exId, rows, d);
                if (!r.Success) problems.Add($"{p.Username} sets {d:MMM d}: {r.Error}");
            }
        }

        // Weigh-ins every few days, trending a little
        for (var i = 0; i <= 7 * p.Weeks; i += 3)
        {
            var date = start.AddDays(i);
            var kg = Math.Round(p.WeightKg - 0.6 * (i / 7.0 / Math.Max(1, p.Weeks)) + (i % 2 == 0 ? 0.2 : -0.1), 1);
            await profiles.LogWeightAsync(userId, kg, date);
        }
    }

    // Each demo person's usual gyms (indexes into the city's gyms by position), so the pins land in different parts of town
    private static readonly Dictionary<string, int[]> GymSpots = new()
    {
        ["alex"] = [4, 5],
        ["maya"] = [7, 8],
        ["jordan"] = [1, 9],
        ["sam"] = [3, 13],
        ["riley"] = [0, 6],
    };

    private static async Task SeedMapAsync(IDbContextFactory<AppDbContext> dbFactory, Dictionary<string, int> ids, List<string> problems)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var city = await db.Cities.OrderBy(c => c.Id).FirstOrDefaultAsync(c => c.Name == "Timișoara");
        if (city is null) { problems.Add("Timișoara isn't in the city list yet (open the Gyms screen once)"); return; }
        var gyms = await db.Gyms.Where(g => g.CityId == city.Id && g.OwnerUserId == null && g.Latitude != null)
            .OrderBy(g => g.Id).ToListAsync();
        if (gyms.Count == 0) { problems.Add("Timișoara has no gyms yet"); return; }

        // Home city for every account, the test ones too
        foreach (var userId in await db.Users.Select(u => u.Id).ToListAsync())
        {
            var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            if (profile is null) { profile = new UserProfile { UserId = userId }; db.UserProfiles.Add(profile); }
            profile.HomeCityId = city.Id;
        }

        var today = AppClock.Today;
        foreach (var (name, spots) in GymSpots)
        {
            if (!ids.TryGetValue(name, out var userId)) continue;
            var mine = spots.Select(i => gyms[i % gyms.Count]).ToList();
            var profile = await db.UserProfiles.FirstAsync(p => p.UserId == userId);
            profile.ShareGymCheckIns = true;
            profile.HomeGymId = mine[0].Id;

            // The last 10 days of workouts happened at one of their gyms (mostly the first), the last one yesterday or today
            var logs = await db.WorkoutLogs.Where(l => l.UserId == userId && l.Date >= today.AddDays(-10))
                .OrderBy(l => l.Date).ToListAsync();
            for (var i = 0; i < logs.Count; i++)
                logs[i].GymId = mine[i % 3 == 2 ? 1 : 0].Id;
        }

        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException e) { problems.Add($"map data: {e.GetBaseException().Message}"); }
    }

    // (badge, tier, days ago) per person; tier is 1 to 4, one-time badges use 1
    private static readonly Dictionary<string, (string Badge, int Tier, int DaysAgo)[]> Badges = new()
    {
        ["alex"] = [("streak", 2, 4), ("century", 1, 20), ("pr-breaker", 2, 9), ("perfect-week", 2, 11), ("heavy-lifter", 2, 15),
                    ("weigh-in-habit", 1, 14), ("fuel-streak", 1, 3), ("exercise-explorer", 1, 25), ("split-finisher", 1, 18),
                    ("regular", 1, 7), ("explorer", 1, 6), ("we-go-jim", 1, 2), ("rep-machine", 1, 12), ("triple-digits", 1, 21)],
        ["maya"] = [("streak", 2, 5), ("century", 1, 22), ("pr-breaker", 1, 8), ("perfect-week", 1, 10), ("we-go-jim", 1, 2)],
        ["jordan"] = [("streak", 1, 12), ("century", 1, 17), ("heavy-lifter", 3, 13), ("pr-breaker", 3, 6), ("triple-digits", 1, 28)],
        ["sam"] = [("streak", 1, 9), ("century", 1, 19), ("explorer", 1, 5)],
        ["riley"] = [("streak", 1, 3), ("weigh-in-habit", 1, 8)],
    };

    private static async Task SeedBadgesAsync(IServiceProvider sp, Dictionary<string, int> ids)
    {
        var badges = sp.GetRequiredService<BadgeService>();
        var today = AppClock.Today;
        foreach (var (name, list) in Badges)
        {
            if (!ids.TryGetValue(name, out var userId)) continue;
            foreach (var (badge, tier, daysAgo) in list)
                await badges.AwardAsync(userId, badge, tier, today.AddDays(-daysAgo));
            await badges.SetFeaturedAsync(userId, list[0].Badge);
        }

        // Everything already earned counts as seen, the app's own ones from the history too, and the "from your history"
        // sheet is spent, so nothing pops up during the presentation
        await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var demoIds = ids.Values.ToList();
        var now = AppClock.Now;
        await db.EarnedBadges.Where(e => demoIds.Contains(e.UserId) && e.SeenAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(e => e.SeenAt, now));
        await db.Users.Where(u => demoIds.Contains(u.Id))
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.BadgesBackfilled, true).SetProperty(x => x.BadgeHistorySheetPending, false));
    }

    private static async Task SeedCaloriesAsync(IServiceProvider sp, int userId, List<string> problems)
    {
        var calories = sp.GetRequiredService<CalorieService>();
        var food = sp.GetRequiredService<FoodService>();
        var today = AppClock.Today;

        var goal = await calories.UseRecommendedAsync(userId, GoalType.Maintain, today);
        if (!goal.Success) problems.Add($"calorie goal: {goal.Error}");

        // A run replaces the week instead of adding to it
        await using (var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
            await db.FoodEntries.Where(f => f.UserId == userId && f.Date >= today.AddDays(-6)).ExecuteDeleteAsync();

        // The meals of a normal day (about 1,935 kcal), scaled to each day's total
        (double P, double C, double F, string Label, Meal Meal)[] day =
        [
            (30, 60, 12, "Oats, milk and banana", Meal.Breakfast),
            (45, 70, 15, "Chicken, rice and veg", Meal.Lunch),
            (20, 15, 10, "Greek yoghurt and nuts", Meal.Snacks),
            (40, 80, 18, "Pasta with beef", Meal.Dinner),
        ];
        var baseKcal = day.Sum(m => 4 * m.P + 4 * m.C + 9 * m.F);
        var goalKcal = (double)((await calories.GetAsync(userId, today))?.Goal.Goal ?? 2400);

        // Six days back to yesterday: on target, under, a little over, on target, under, a little over; today half logged
        double[] share = [1.00, 0.80, 1.15, 0.97, 0.84, 1.17];
        for (var back = 6; back >= 0; back--)
        {
            var date = today.AddDays(-back);
            var scale = back == 0 ? goalKcal * 0.95 / baseKcal : goalKcal * share[6 - back] / baseKcal;
            var meals = back == 0 ? day.Take(2) : day;
            foreach (var m in meals)
            {
                var r = await food.QuickAddMacrosAsync(userId, Math.Round(m.P * scale), Math.Round(m.C * scale), Math.Round(m.F * scale), m.Label, m.Meal, date);
                if (!r.Success) { problems.Add($"food {date:MMM d}: {r.Error}"); break; }
            }
        }
    }
}
