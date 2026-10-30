using GymApp.Client;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using GymApp.Services;
using Microsoft.EntityFrameworkCore;

// Seeds the account the store screenshots are taken with (docs/STORE_LISTING.md): "alex" with six weeks of a
// Push / Pull / Legs split, PRs, weigh-ins and calories, and three friends with their own history and gym check-ins
// in Timișoara. It runs the app's own services against a social server, so a development server only (the passwords
// below are test values). History is written with the clock shifted back and no syncing; each user then uploads once
// at the real today, as a phone would. Afterwards gymapp.db in the output folder goes onto a phone, and signing in as
// alex there picks up the history (see docs/STORE_LISTING.md, "Screenshots").
//
//   dotnet run --project tools/GymApp.ScreenshotSeed -- <output folder> [server url, default http://localhost:5080]

if (args.Length < 1) { Console.Error.WriteLine("Usage: GymApp.ScreenshotSeed <output folder> [server url]"); return 1; }
var outDir = Path.GetFullPath(args[0]);
var server = new Uri(args.Length > 1 ? args[1] : "http://localhost:5080");
if (Directory.Exists(outDir) && Directory.EnumerateFileSystemEntries(outDir).Any())
{
    Console.Error.WriteLine($"{outDir} isn't empty. Use a new folder.");
    return 1;
}

const string Password = "Screenshots-2026";
const string City = "Timișoara";
var realToday = DateOnly.FromDateTime(DateTime.Now);

AppDbContext.DataDirectory = outDir;
OsmHttp.UserAgent = "GainStreak/screenshot-seed (macOS; student project)";
IDbContextFactory<AppDbContext> dbf = new DbFactory();
await using (var db = await dbf.CreateDbContextAsync()) await db.Database.MigrateAsync();

var tokens = new MemoryTokenStore();
var api = new SocialApiClient(new HttpClient { BaseAddress = server }, tokens, appVersion: typeof(AppDbContext).Assembly.GetName().Version!.ToString(3));
var session = new SessionService();
var accounts = new AccountService(api, dbf, tokens, session, "iPhone");
var friends = new RemoteFriendService(api, new SocialCacheStore(dbf), session);
var sync = new SocialSync(api, dbf, session, new StatsProjectionService(dbf));

// History never syncs: the server only takes dates near its own today. Each user uploads once at the end.
var none = NoSocialSync.Instance;
var points = new PointsService(dbf, none);
var workouts = new WorkoutService(dbf, new StreakService(dbf), points, none);
var splits = new SplitService(dbf, points, none);
var dayExercises = new DayExerciseService(dbf);
var sets = new SetService(dbf);
var profiles = new ProfileService(dbf, none);
var calories = new CalorieService(dbf, points);
var gyms = new GymService(dbf, none);

// The city and its gyms, from OpenStreetMap as on a phone
var found = await new GeocodingService().SearchAsync(City);
if (!found.Success || found.Candidates.Count == 0) throw new Exception($"City search failed: {found.Error}");
var city = await new CityService(dbf).UpsertAsync(found.Candidates[0]);
// Overpass is often busy: a few tries
var fetched = await new GymFetchService(dbf).RefreshAsync(city.Id);
for (var attempt = 1; attempt < 4 && fetched.Outcome == GymRefreshOutcome.Failed; attempt++)
{
    Console.WriteLine($"Loading gyms failed ({fetched.Error}); trying again.");
    await Task.Delay(TimeSpan.FromSeconds(20));
    fetched = await new GymFetchService(dbf).RefreshAsync(city.Id);
}
List<Gym> cityGyms;
await using (var db = await dbf.CreateDbContextAsync())
    cityGyms = await db.Gyms.AsNoTracking().Where(g => g.CityId == city.Id && g.Name != "").OrderBy(g => g.Name).ToListAsync();
if (cityGyms.Count < 4) throw new Exception($"Only {cityGyms.Count} gyms in {City} ({fetched.Outcome} {fetched.Error}).");
Console.WriteLine($"{City}: {cityGyms.Count} gyms");

var people = new[]
{
    new Person("alex", "Push / Pull / Legs", Weeks: 6, Sex.Male, new DateOnly(2001, 4, 12), 181, 82.4, -0.15, cityGyms[0], Main: true),
    new Person("maya", "Upper / Lower", Weeks: 5, Sex.Female, new DateOnly(2002, 9, 3), 167, 61.0, -0.1, cityGyms[1]),
    new Person("jonas", "Push / Pull / Legs", Weeks: 4, Sex.Male, new DateOnly(1999, 1, 27), 186, 90.5, 0.1, cityGyms[2]),
    new Person("sam", "Full Body", Weeks: 3, Sex.Male, new DateOnly(2003, 6, 18), 175, 72.0, 0.05, cityGyms[3]),
};

// The server allows 5 sign-ups an hour and 10 sign-ins a minute per IP, so a rerun signs in to the accounts an earlier
// run made (the server's stats and check-ins are replaced by this run's uploads), and each user signs in once or twice.
// The friends go first, so alex can ask them.
var ids = new Dictionary<string, (int Local, int Server)>();
foreach (var p in people.OrderBy(p => p.Main))
{
    if (session.CurrentUser is not null) await accounts.LogOutAsync();
    var r = await accounts.SignInAsync(p.Name, Password);
    if (!r.Success) r = await accounts.RegisterAsync(p.Name, $"{p.Name}@example.test", Password);
    if (!r.Success) throw new Exception($"Signing in or registering {p.Name}: {r.Error}");
    ids[p.Name] = (session.CurrentUser!.Id, session.CurrentUser.ServerUserId!.Value);
    await SeedHistoryAsync(p, ids[p.Name].Local);
    await UploadAsync(p.Name, ids[p.Name].Local);
    Console.WriteLine($"{p.Name}: history from {realToday.AddDays(-7 * p.Weeks)}");
}

// alex, still signed in, asks; the others accept. Friends from an earlier run stay friends.
var alex = ids["alex"];
var already = (await friends.GetFriendsAsync(alex.Local)).Select(f => f.Username).ToHashSet();
var asking = people.Where(p => !p.Main && !already.Contains(p.Name)).ToList();
foreach (var p in asking)
    Check((await friends.SendRequestAsync(alex.Local, ids[p.Name].Server)).Success, $"request to {p.Name}");
foreach (var p in asking)
{
    await SignInAsync(p.Name);
    Check((await friends.AcceptAsync(ids[p.Name].Local, alex.Server)).Success, $"{p.Name} accepting");
}
if (session.CurrentUser is not null) await accounts.LogOutAsync();

Console.WriteLine($"Done. {Path.Combine(outDir, "gymapp.db")} holds all four; sign in on the phone as alex.");
return 0;

async Task SeedHistoryAsync(Person p, int userId)
{
    var start = realToday.AddDays(-7 * p.Weeks);
    start = start.AddDays(-(((int)start.DayOfWeek + 6) % 7));   // the Monday of that week
    ShiftTo(start);

    Check((await profiles.SaveAsync(userId, new ProfileInput(p.Sex, p.Birth, p.HeightCm, ActivityLevel.Active, UnitSystem.Metric), start)).Success, "profile");
    Check((await profiles.SetHomeCityAsync(userId, city.Id)).Success, "home city");
    Check((await profiles.SetHomeGymAsync(userId, p.Gym.Id)).Success, "home gym");
    Check((await profiles.SetAskForGymAfterCheckInAsync(userId, false)).Success, "ask for gym");
    Check((await profiles.SetShareGymCheckInsAsync(userId, true)).Success, "share check-ins");
    Check((await profiles.SetThemeAsync(userId, ThemePreference.Dark)).Success, "theme");

    // The split, from the first day of the history, with its day lists
    WorkoutSplit split;
    await using (var db = await dbf.CreateDbContextAsync())
        split = await db.WorkoutSplits.AsNoTracking().Include(s => s.Days).Include(s => s.Variants)
            .FirstAsync(s => s.OwnerUserId == null && s.Name == p.Split);
    var variant = split.Variants.FirstOrDefault(v => v.IsDefault) ?? split.Variants[0];
    // The week starts on today's weekday, so today is a training day (Home shows today's workout)
    Check(await splits.SelectAsync(userId, split.Id, variant.Id, realToday.DayOfWeek), "split");

    var available = await new ExerciseService(dbf).GetAvailableAsync(userId);
    var plans = new Dictionary<string, List<(int ExerciseId, double StartKg, int Reps)>>();
    foreach (var day in split.Days)
    {
        var list = new List<(int, double, int)>();
        foreach (var (name, kg, reps) in DayPlan(day.Name, p.Sex))
        {
            var exercise = available.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? throw new Exception($"No built-in exercise named {name}.");
            Check((await dayExercises.AddAsync(userId, day.Id, exercise.Id, warmupSets: kg >= 60 ? 1 : 0, workingSets: 3)).Success, name);
            list.Add((exercise.Id, kg * (p.Sex == Sex.Female ? 0.55 : 1), reps));
        }
        plans[day.Name] = list;
    }

    // Every training day up to yesterday, weights creeping up so PRs come along; calories most days, weigh-ins weekly
    var rng = new Random(p.Name.GetHashCode(StringComparison.Ordinal) & 0xFFFF);
    Check((await calories.SetGoalTypeAsync(userId, p.WeightTrend < 0 ? GoalType.Lose : p.WeightTrend > 0 ? GoalType.Gain : GoalType.Maintain)).Success, "goal type");
    var goalSet = false;
    for (var d = start; d < realToday; d = d.AddDays(1))
    {
        ShiftTo(d);
        var week = (d.DayNumber - start.DayNumber) / 7;
        if (d.DayOfWeek == DayOfWeek.Monday)
            Check((await profiles.LogWeightAsync(userId, Math.Round(p.WeightKg + p.WeightTrend * 7 * week + rng.NextDouble() * 0.4, 1), d)).Success, "weight");
        if (!goalSet) { Check((await calories.AcceptSuggestionAsync(userId, d)).Success, "calorie goal"); goalSet = true; }

        var today = await workouts.GetTodayWorkoutAsync(userId, d);
        if (today.DayName is { } dayName)
        {
            await LogSetsAsync(userId, plans[dayName], week, d);
            var log = await workouts.LogTodayAsync(userId, today: d);
            Check(log.Success, $"{p.Name} {d}: {log.Error}");
        }

        var goal = (await calories.GetAsync(userId, d))?.Goal.Goal ?? 2400;
        if (rng.Next(7) != 0) Check((await calories.LogTodayAsync(userId, goal + rng.Next(-250, 200), d)).Success, "calories");
    }

    // alex hasn't started today's workout (Home shows it planned); the others are done for the day
    ShiftTo(realToday);
    var now = await workouts.GetTodayWorkoutAsync(userId, realToday);
    if (!p.Main && now.DayName is { } todayName)
    {
        await LogSetsAsync(userId, plans[todayName], (realToday.DayNumber - start.DayNumber) / 7, realToday);
        Check((await workouts.LogTodayAsync(userId, today: realToday)).Success, $"{p.Name} today");
    }
    AppClock.Offset = TimeSpan.Zero;
}

async Task LogSetsAsync(int userId, List<(int ExerciseId, double StartKg, int Reps)> plan, int week, DateOnly d)
{
    foreach (var (exerciseId, startKg, reps) in plan)
    {
        var kg = RoundTo(startKg * (1 + 0.025 * week), 2.5);
        var rows = new List<SetInput>();
        if (startKg >= 60) rows.Add(new SetInput(SetKind.Warmup, 10, RoundTo(kg * 0.5, 2.5)));
        rows.Add(new SetInput(SetKind.Working, reps, kg));
        rows.Add(new SetInput(SetKind.Working, reps, kg));
        rows.Add(new SetInput(SetKind.Working, Math.Max(reps - 2, 3), kg));
        var r = await sets.SaveDayAsync(userId, exerciseId, rows, d);
        Check(r.Success, $"sets {d}: {r.Error}");
    }
}

// The sharing setting, check-ins and stats, at the real today
async Task UploadAsync(string name, int id)
{
    sync.MarkSharingDirty(id);
    sync.MarkCheckInsDirty(id);
    sync.MarkStatsDirty(id);
    await sync.WhenIdleAsync();
    await using var db = await dbf.CreateDbContextAsync();
    var error = await db.SyncStates.Where(s => s.UserId == id).Select(s => s.LastError).FirstOrDefaultAsync();
    if (error is not null) throw new Exception($"{name}'s upload: {error}");
}

async Task SignInAsync(string name)
{
    if (session.CurrentUser is not null) await accounts.LogOutAsync();
    var r = await accounts.SignInAsync(name, Password);
    if (!r.Success) throw new Exception($"Signing in as {name}: {r.Error}");
}

void ShiftTo(DateOnly d) => AppClock.Offset = TimeSpan.FromDays(d.DayNumber - realToday.DayNumber);

static double RoundTo(double kg, double step) => Math.Max(step, Math.Round(kg / step) * step);

static void Check(bool ok, string what)
{
    if (!ok) throw new Exception($"Failed: {what}");
}

// (exercise, starting working weight for a man, reps); women start at 55%
static IEnumerable<(string Name, double Kg, int Reps)> DayPlan(string day, Sex sex) => day switch
{
    "Push" => [("Bench Press", 80, 8), ("Overhead Press", 47.5, 8), ("Incline Dumbbell Press", 26, 10), ("Dumbbell Lateral Raise", 10, 15), ("Rope Pushdown", 25, 12)],
    "Pull" => [("Barbell Row", 70, 8), ("Lat Pulldown", 60, 10), ("Chest-Supported Row", 30, 10), ("Face Pull", 20, 15), ("Barbell Curl", 30, 10)],
    "Legs" => [("Squat", 100, 6), ("Romanian Deadlift", 90, 8), ("Leg Press", 160, 10), ("Lying Leg Curl", 45, 12), ("Standing Calf Raise", 60, 15)],
    "Upper" => [("Bench Press", 75, 8), ("Barbell Row", 65, 8), ("Overhead Press", 45, 8), ("Lat Pulldown", 55, 10), ("Barbell Curl", 27.5, 10)],
    "Lower" => [("Squat", 95, 6), ("Romanian Deadlift", 85, 8), ("Leg Press", 150, 10), ("Lying Leg Curl", 40, 12), ("Standing Calf Raise", 55, 15)],
    _ => [("Squat", 90, 6), ("Bench Press", 72.5, 8), ("Barbell Row", 62.5, 8), ("Overhead Press", 42.5, 8), ("Barbell Curl", 25, 10)],
};

record Person(string Name, string Split, int Weeks, Sex Sex, DateOnly Birth, double HeightCm, double WeightKg, double WeightTrend,
    Gym Gym, bool Main = false);

sealed class DbFactory : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => new();
}
