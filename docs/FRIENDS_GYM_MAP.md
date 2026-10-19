# Friends gym map (Gyms screen rework)

Target version: **1.1.0**. It adds two columns and a migration, so it doesn't fit the 1.0.x "bug fixes only" rule. Friends only exist between accounts on the same install until 10f (sync server), so the friends layer will mostly be empty until then, but everything here works and can be tested locally behind `ISocialStore`.

The design is the **Gyms (Map)** board in the Gym App canvas (1440×900, fits one screen).

## What it does

### Screen layout
- **Header (one line):** "Gyms", the home city with Change city, and two setting chips on the right: **Visible to friends** and **Ask for gym**. The whole chip is the switch.
- **Left:** the friends map, filling the full height.
- **Right, three cards stacked:**
  - **Your gym:** your gym (chosen once), with Change, plus a "Today" line with a "Trained elsewhere?" / Change button.
  - **Friends:** the list of friends' last check-ins, with a Recent / By gym toggle. It scrolls inside the card.
  - **Your check-ins:** the existing monthly summary, in a compact form.
- **Gym picker pop-up:** the gym list (search, A–Z / Nearest, refresh, add custom gym) is a dialog, no longer a card. It opens in two modes:
  - **"Choose your gym"** sets your gym (buttons Set / Current).
  - **"Where did you train today?"** changes today's log only (buttons Pick / Picked).
- **Smaller windows:** below 1100 px wide or 700 px high, the page goes back to normal scrolling and the columns stack.

### Friends map
- Gyms show as small neutral dots. Your gym is a white dot, and today's gym is an accent dot.
- Friends who checked in at a gym show as initial circles on top of it. Your own pin is in the accent colour.
- Only check-ins from the **last 30 days** are shown.
- **Range chips:** Today, 7 days, 30 days.
- **Mode:** **Latest** (one pin per friend, the default) or **All visits** (one entry per friend per gym, at the newest visit).
- **Recency fade:** today 100%, 1 to 2 days 85%, 3 to 7 days 60%, 8 to 29 days 35%.
- **Shared pins:** several friends at one gym share one pin, showing the newest friend's initials plus a "+N" badge.
- **Gym card:** clicking a pin or a gym dot opens a card with:
  - who was there and when
  - your visits this month
  - an **I trained here today** button, which sets today's gym.
- **Other cities:** friends whose check-in was in another city never get a pin. They appear in the Friends card under Elsewhere, or under the city name.
- **Custom gyms** are shared by name only, never as a pin, because a custom gym's address could be someone's home.
- Only the **day** is shared, never a time.

### Friends card
- **Recent (default):** groups Today / This week / Earlier this month / Elsewhere, newest first, each with a count. Each row shows avatar, name, gym and a day badge.
- **By gym:** one group per gym, ordered nearest to you first, with the distance in the group header.
  - Your gym is marked "· your gym".
  - Other cities come last, one group per city.
- The list uses the same range as the map.
- Clicking a row selects that gym on the map. Rows for other cities can't be clicked.

### Your gym
- New `UserProfile.HomeGymId`, chosen once from the pop-up.
- **With "Ask for gym" on:** the check-in prompt opens with your gym preselected.
- **With it off:** every check-in uses your gym automatically.
- "Trained elsewhere?" logs a different gym for today only, and your gym stays the same.

### Sharing
- Sharing is opt-in: `UserProfile.ShareGymCheckIns`.
  - `null` = not asked yet
  - `false` = no
  - `true` = yes
- The user is asked once, after their first check-in with a gym, and can change it later with the header chip or in Profile.
- Turning it off hides all past check-ins immediately, because the map is a query, not a copy.
- **Nothing is deleted after 30 days.** WorkoutLogs stay forever because streaks and points are recomputed from them. The 30 days is a query filter.
- Undo check-in, Change gym, and "I trained here today" update the map automatically.

## Files at a glance

| Project | File | New or change |
| --- | --- | --- |
| GymApp.Data | `Entities/UserProfile.cs` | Change: add `ShareGymCheckIns` and `HomeGymId` |
| GymApp.Data | `AppDbContext.cs` (OnModelCreating) | Change: `HomeGym` relationship |
| GymApp.Data | Migration `AddGymSharingAndHomeGym` | New (generated) |
| GymApp.Core | `Social/FriendMapRules.cs` | New: map rules plus friends-list grouping |
| GymApp.Services | `Social/ISocialStore.cs` | Change: add one method |
| GymApp.Services | `Social/LocalSocialStore.cs` | Change: implement it |
| GymApp.Services | `Social/FriendMapService.cs` | New |
| GymApp.Services | `ProfileService.cs` | Change: `SetShareGymCheckInsAsync`, `SetHomeGymAsync` |
| GymApp.Services | `WorkoutService.cs` (or wherever the gym is attached to today's log) | Change: default to the home gym |
| GymApp.Desktop | `Map/FriendMapLayer.cs` | New: Mapsui layer |
| GymApp.Desktop | `ViewModels/GymsViewModel.cs` | Change: map state, friends grouping, your gym, picker dialog, sharing |
| GymApp.Desktop | `ViewModels/FriendRowViewModel.cs`, `FriendGroupViewModel.cs` | New |
| GymApp.Desktop | `Views/GymsView.axaml` | Change: fit-to-screen two-column layout |
| GymApp.Desktop | `Views/GymPickerDialog.axaml` (+ `.cs`) | New: the gym list as a dialog (moved out of GymsView) |
| GymApp.Desktop | Check-in flow (gym picker prompt) | Change: preselect your gym, ask about sharing once |
| GymApp.Tests | `FriendMapRulesTests.cs`, `FriendMapServiceTests.cs`, `HomeGymTests.cs` | New |
| docs | `GymApp_Implementation_Plan`, `PHASE_10_FRIENDS_LEADERBOARD.md` | Change: privacy line, data model |

Names like `AppDbContext`, `FriendshipStatus`, the `Entities` folder, or `IClock` are my guesses at what you called things. Swap in your real names. I also assume `WorkoutLog.Date` is a `DateOnly`; if it's a `DateTime`, compare on `.Date`.

---

## 1. Data (GymApp.Data)

### 1a. `UserProfile.cs`: add two properties

```csharp
/// <summary>
/// Share gym check-ins (gym + day, never the time) with accepted friends.
/// null = not asked yet, false = declined, true = shared.
/// </summary>
public bool? ShareGymCheckIns { get; set; }

/// <summary>The user's usual gym, chosen once. Preselected (or used automatically) at check-in.</summary>
public int? HomeGymId { get; set; }
public Gym? HomeGym { get; set; }
```

Existing rows become `null` for both, which means not shared and no gym chosen. Nobody's location appears on the map without them agreeing.

### 1b. `AppDbContext.cs`: relationship for `HomeGym`

In `OnModelCreating`, next to the existing UserProfile configuration:

```csharp
modelBuilder.Entity<UserProfile>()
    .HasOne(p => p.HomeGym)
    .WithMany()
    .HasForeignKey(p => p.HomeGymId)
    .OnDelete(DeleteBehavior.SetNull); // deleting a custom gym just clears "your gym"
```

### 1c. Migration

From the solution folder:

```
dotnet ef migrations add AddGymSharingAndHomeGym --project GymApp.Data --startup-project GymApp.Desktop
```

Check that `Up` only adds the two columns, the index and the foreign key, with no data changes.

**Optional backfill:** set `HomeGymId` to each user's most-used gym, so existing users don't start empty. If you want it, add this to `Up` after the columns:

```csharp
migrationBuilder.Sql(@"
UPDATE UserProfiles SET HomeGymId = (
    SELECT w.GymId FROM WorkoutLogs w
    WHERE w.UserId = UserProfiles.UserId AND w.GymId IS NOT NULL
    GROUP BY w.GymId ORDER BY COUNT(*) DESC LIMIT 1)
WHERE HomeGymId IS NULL;");
```

Check the table names against your migrations first.

---

## 2. Core (GymApp.Core): `Social/FriendMapRules.cs` (new file)

All the rules live here so they're unit-testable, like StreakCalculator and the PR logic. That includes the map, the range and mode, and the two friends-list groupings.

```csharp
using System.Globalization;

namespace GymApp.Core.Social;

public enum MapRange { Today, Week, Month }

public enum MapMode { Latest, AllVisits }

public enum FriendSort { Recent, ByGym }

/// <summary>One friend check-in at a gym. Coordinates are null for gyms without them.</summary>
public sealed record FriendCheckIn(
    int FriendUserId,
    string Username,
    int GymId,
    string GymName,
    int? CityId,
    string? CityName,
    double? Latitude,
    double? Longitude,
    double? DistanceKm,          // from the user's home city centre (or position), null if unknown
    bool IsCustomGym,
    DateOnly Date);

/// <summary>A pin on the map: one gym and the friends shown there, newest first.</summary>
public sealed record GymPin(
    int GymId,
    string GymName,
    double Latitude,
    double Longitude,
    IReadOnlyList<FriendCheckIn> Visitors,
    int NewestDaysAgo)
{
    public FriendCheckIn Newest => Visitors[0];
    public int MoreCount => Visitors.Count - 1;
}

public enum FriendGroupKind { Today, ThisWeek, Earlier, Elsewhere, Gym, City }

/// <summary>A section of the Friends card.</summary>
public sealed record FriendGroup(
    FriendGroupKind Kind,
    string Title,               // "Today", "World Class", "Cluj-Napoca", ...
    int? GymId,                 // set for Kind == Gym
    double? DistanceKm,         // set for Kind == Gym
    IReadOnlyList<FriendCheckIn> Rows,
    bool OnMap);                // false for Elsewhere / City: rows can't be clicked

/// <summary>Everything the Gyms screen needs for the friends layer.</summary>
public sealed record FriendMapLayout(
    IReadOnlyList<GymPin> Pins,
    IReadOnlyList<FriendCheckIn> LatestPerFriend); // feeds the Friends card

public static class FriendMapRules
{
    public const int WindowDays = 30;

    /// <summary>First day still inside the 30-day window (today counts as day 1).</summary>
    public static DateOnly WindowStart(DateOnly today) => today.AddDays(-(WindowDays - 1));

    public static int DaysAgo(DateOnly date, DateOnly today) => today.DayNumber - date.DayNumber;

    public static int RangeDays(MapRange range) => range switch
    {
        MapRange.Today => 1,
        MapRange.Week => 7,
        MapRange.Month => WindowDays,
        _ => throw new ArgumentOutOfRangeException(nameof(range))
    };

    public static bool InRange(DateOnly date, DateOnly today, MapRange range)
    {
        var d = DaysAgo(date, today);
        return d >= 0 && d < RangeDays(range); // future dates never show
    }

    /// <summary>Recency fade for pins and avatars.</summary>
    public static double Opacity(int daysAgo) => daysAgo switch
    {
        <= 0 => 1.0,
        <= 2 => 0.85,
        <= 7 => 0.6,
        _ => 0.35
    };

    /// <summary>"Today", "Yesterday", "3 days ago", then "12 Oct". Day only, never a time.</summary>
    public static string DescribeDay(DateOnly date, DateOnly today) => DaysAgo(date, today) switch
    {
        0 => "Today",
        1 => "Yesterday",
        var d and > 1 and < 7 => $"{d} days ago",
        _ => date.ToString("d MMM", CultureInfo.InvariantCulture)
    };

    /// <summary>A check-in can be a pin when it's a built-in gym with coordinates in the home city.</summary>
    public static bool IsOnMap(FriendCheckIn c, int? homeCityId) =>
        !c.IsCustomGym && c.Latitude is not null && c.Longitude is not null &&
        homeCityId is not null && c.CityId == homeCityId;

    /// <summary>
    /// Applies range and mode. Latest = one entry per friend (their newest check-in).
    /// AllVisits = one entry per friend per gym (newest visit there), so a pin never
    /// shows the same friend twice.
    /// </summary>
    public static IReadOnlyList<FriendCheckIn> Select(
        IEnumerable<FriendCheckIn> all, DateOnly today, MapRange range, MapMode mode)
    {
        var inRange = all.Where(c => InRange(c.Date, today, range));

        var picked = mode == MapMode.Latest
            ? inRange.GroupBy(c => c.FriendUserId).Select(g => g.MaxBy(c => c.Date)!)
            : inRange.GroupBy(c => (c.FriendUserId, c.GymId)).Select(g => g.MaxBy(c => c.Date)!);

        return picked
            .OrderByDescending(c => c.Date)
            .ThenBy(c => c.Username, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Pins for the map plus each friend's newest check-in for the Friends card.</summary>
    public static FriendMapLayout Layout(
        IEnumerable<FriendCheckIn> all, int? homeCityId, DateOnly today, MapRange range, MapMode mode)
    {
        var list = all as IReadOnlyCollection<FriendCheckIn> ?? all.ToList();
        var selected = Select(list, today, range, mode);

        var pins = selected
            .Where(c => IsOnMap(c, homeCityId))
            .GroupBy(c => c.GymId)
            .Select(g =>
            {
                var visitors = g.OrderByDescending(c => c.Date)
                                .ThenBy(c => c.Username, StringComparer.OrdinalIgnoreCase)
                                .ToList();
                var first = visitors[0];
                return new GymPin(first.GymId, first.GymName, first.Latitude!.Value,
                                  first.Longitude!.Value, visitors, DaysAgo(first.Date, today));
            })
            .OrderBy(p => p.NewestDaysAgo)
            .ToList();

        // The Friends card always uses each friend's newest check-in in range, in either map mode.
        var latest = Select(list, today, range, MapMode.Latest);

        return new FriendMapLayout(pins, latest);
    }

    /// <summary>Sections of the Friends card for the chosen sort. Empty sections are left out.</summary>
    public static IReadOnlyList<FriendGroup> GroupFriends(
        IReadOnlyList<FriendCheckIn> latestPerFriend, FriendSort sort,
        int? homeCityId, int? homeGymId, DateOnly today)
    {
        var onMap = latestPerFriend.Where(c => IsOnMap(c, homeCityId)).ToList();
        var away = latestPerFriend.Where(c => !IsOnMap(c, homeCityId)).ToList();
        var groups = new List<FriendGroup>();

        static List<FriendCheckIn> Newest(IEnumerable<FriendCheckIn> rows) =>
            rows.OrderByDescending(c => c.Date)
                .ThenBy(c => c.Username, StringComparer.OrdinalIgnoreCase).ToList();

        if (sort == FriendSort.Recent)
        {
            void Add(FriendGroupKind kind, string title, IEnumerable<FriendCheckIn> rows, bool clickable)
            {
                var list = Newest(rows);
                if (list.Count > 0) groups.Add(new FriendGroup(kind, title, null, null, list, clickable));
            }

            Add(FriendGroupKind.Today, "Today", onMap.Where(c => DaysAgo(c.Date, today) == 0), true);
            Add(FriendGroupKind.ThisWeek, "This week", onMap.Where(c => DaysAgo(c.Date, today) is >= 1 and < 7), true);
            Add(FriendGroupKind.Earlier, "Earlier this month", onMap.Where(c => DaysAgo(c.Date, today) >= 7), true);
            Add(FriendGroupKind.Elsewhere, "Elsewhere", away, false);
            return groups;
        }

        // ByGym: nearest gym first (unknown distance last, then by name), other cities last.
        groups.AddRange(onMap
            .GroupBy(c => c.GymId)
            .Select(g => new
            {
                First = g.First(),
                Rows = Newest(g)
            })
            .OrderBy(x => x.First.DistanceKm ?? double.MaxValue)
            .ThenBy(x => x.First.GymName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new FriendGroup(
                FriendGroupKind.Gym,
                x.First.GymId == homeGymId ? $"{x.First.GymName} · your gym" : x.First.GymName,
                x.First.GymId, x.First.DistanceKm, x.Rows, true)));

        groups.AddRange(away
            .GroupBy(c => c.CityName ?? "Other places")
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new FriendGroup(FriendGroupKind.City, g.Key, null, null, Newest(g), false)));

        return groups;
    }

    /// <summary>Two-letter initials for a username, matching the Friends screen.</summary>
    public static string Initials(string username)
    {
        var letters = new string(username.Where(char.IsLetter).Take(2).ToArray());
        return letters.Length == 0 ? "?" : letters.ToUpperInvariant();
    }

    /// <summary>Straight-line distance in km (haversine), for "By gym" ordering.</summary>
    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0;
        static double Rad(double d) => d * Math.PI / 180.0;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}
```

Custom gyms are never pins, and in the Friends card they fall under **Elsewhere** (Recent) or the city group (By gym), shown by name only. If you'd rather list them under "Custom gyms", add a `FriendGroupKind.Custom` and split `away` by `IsCustomGym`.

---

## 3. Services (GymApp.Services)

### 3a. `ISocialStore.cs`: add one method

```csharp
/// <summary>
/// Gym check-ins of the user's accepted friends since <paramref name="since"/> (inclusive),
/// only for friends who share them and only for logs with a gym.
/// </summary>
Task<IReadOnlyList<FriendCheckIn>> GetFriendGymCheckInsAsync(
    int userId, DateOnly since, CancellationToken ct = default);
```

Add `using GymApp.Core.Social;` at the top.

### 3b. `LocalSocialStore.cs`: implement it

```csharp
public async Task<IReadOnlyList<FriendCheckIn>> GetFriendGymCheckInsAsync(
    int userId, DateOnly since, CancellationToken ct = default)
{
    var friendIds = _db.Friendships
        .Where(f => f.Status == FriendshipStatus.Accepted &&
                    (f.UserAId == userId || f.UserBId == userId))
        .Select(f => f.UserAId == userId ? f.UserBId : f.UserAId);

    var query =
        from log in _db.WorkoutLogs
        where log.GymId != null && log.Date >= since && friendIds.Contains(log.UserId)
        join profile in _db.UserProfiles on log.UserId equals profile.UserId
        where profile.ShareGymCheckIns == true
        join user in _db.Users on log.UserId equals user.Id
        join gym in _db.Gyms on log.GymId equals gym.Id
        join city in _db.Cities on gym.CityId equals city.Id into cities
        from city in cities.DefaultIfEmpty()
        select new FriendCheckIn(
            user.Id,
            user.Username,
            gym.Id,
            gym.Name,
            gym.CityId,
            city != null ? city.Name : null,
            gym.OwnerUserId == null ? gym.Latitude : null,   // custom gyms: never coordinates
            gym.OwnerUserId == null ? gym.Longitude : null,
            null,                                            // distance filled in by FriendMapService
            gym.OwnerUserId != null,
            log.Date);

    return await query.AsNoTracking().ToListAsync(ct);
}
```

Notes:
- The custom-gym rule is enforced here as well as in Core: a custom gym's coordinates never leave the store. When the sync server exists, its contract should follow the same rule.
- `profile.ShareGymCheckIns == true` (not `!= false`) is deliberate: `null` means not asked, so not shared.
- If `Gym.Latitude`/`Longitude` aren't nullable in your entity, drop the casts accordingly.

### 3c. `Social/FriendMapService.cs` (new file)

```csharp
using GymApp.Core.Social;

namespace GymApp.Services.Social;

public sealed record FriendMapResult(FriendMapLayout Layout, IReadOnlyList<FriendGroup> Groups);

public sealed class FriendMapService(ISocialStore social)
{
    /// <param name="origin">Where distances are measured from: the home gym if set, else the home city centre.</param>
    public async Task<FriendMapResult> GetAsync(
        int userId, int? homeCityId, int? homeGymId, (double Lat, double Lon)? origin,
        DateOnly today, MapRange range, MapMode mode, FriendSort sort,
        CancellationToken ct = default)
    {
        var raw = await social.GetFriendGymCheckInsAsync(
            userId, FriendMapRules.WindowStart(today), ct);

        var withDistance = origin is { } o
            ? raw.Select(c => c.Latitude is { } lat && c.Longitude is { } lon
                    ? c with { DistanceKm = FriendMapRules.DistanceKm(o.Lat, o.Lon, lat, lon) }
                    : c).ToList()
            : raw.ToList();

        var layout = FriendMapRules.Layout(withDistance, homeCityId, today, range, mode);
        var groups = FriendMapRules.GroupFriends(layout.LatestPerFriend, sort, homeCityId, homeGymId, today);
        return new FriendMapResult(layout, groups);
    }
}
```

Register it wherever you register FriendService and LeaderboardService. Pass `today` from the same place the streak code gets it (your clock abstraction, if you have one) so tests can fix the date.

Switching the sort doesn't need the database. The ViewModel can call `FriendMapRules.GroupFriends` again on the `LatestPerFriend` it already has (see 4c).

### 3d. `ProfileService.cs`: two setters

```csharp
public async Task SetShareGymCheckInsAsync(int userId, bool share, CancellationToken ct = default)
{
    var profile = await _db.UserProfiles.SingleAsync(p => p.UserId == userId, ct);
    profile.ShareGymCheckIns = share;
    await _db.SaveChangesAsync(ct);
}

/// <summary>Sets (or clears, with null) the user's gym. Custom gyms must be the user's own.</summary>
public async Task SetHomeGymAsync(int userId, int? gymId, CancellationToken ct = default)
{
    if (gymId is { } id)
    {
        var ok = await _db.Gyms.AnyAsync(g => g.Id == id && g.IsActive &&
                                              (g.OwnerUserId == null || g.OwnerUserId == userId), ct);
        if (!ok) throw new InvalidOperationException("That gym can't be chosen.");
    }

    var profile = await _db.UserProfiles.SingleAsync(p => p.UserId == userId, ct);
    profile.HomeGymId = gymId;
    await _db.SaveChangesAsync(ct);
}
```

If a user has no UserProfile row yet, create it the same way the profile screen does. Throw whatever exception type your 12d validation uses, so the message shows next to the field.

### 3e. Check-in: default to the home gym

Where today's gym is attached to the WorkoutLog after "I went today" (the Phase 9 gym prompt and `WorkoutService` or `GymService`):

```csharp
// After the WorkoutLog for today is created:
var profile = await _db.UserProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId, ct);

if (profile?.AskForGymAfterCheckIn == false && profile.HomeGymId is { } homeGymId)
{
    log.GymId = homeGymId;          // "Ask for gym" off: use your gym automatically
    await _db.SaveChangesAsync(ct);
}
// "Ask for gym" on: the prompt opens with profile.HomeGymId preselected (see 4f).
```

The existing "Add gym" / "Change gym" link on Home and the new "Trained elsewhere?" button both set today's `GymId` only. They never touch `HomeGymId`.

---

## 4. Desktop (GymApp.Desktop)

### 4a. `ViewModels/FriendRowViewModel.cs` and `FriendGroupViewModel.cs` (new files)

```csharp
using GymApp.Core.Social;

namespace GymApp.Desktop.ViewModels;

public sealed class FriendRowViewModel(FriendCheckIn c, DateOnly today, bool onMap, bool byGym)
{
    public int FriendUserId => c.FriendUserId;
    public int GymId => c.GymId;
    public string Username => c.Username;
    public string Initials => FriendMapRules.Initials(c.Username);
    public string When => FriendMapRules.DescribeDay(c.Date, today);
    public bool IsToday => FriendMapRules.DaysAgo(c.Date, today) == 0;   // accent day badge
    public double Opacity => FriendMapRules.Opacity(FriendMapRules.DaysAgo(c.Date, today));
    public bool CanShow => onMap;                                         // other cities can't be clicked

    /// <summary>Second line of the row: the gym in Recent, a hint in By gym (the gym is the header).</summary>
    public string Subtitle => byGym
        ? (onMap ? "Show on the map" : c.GymName)
        : c.IsCustomGym ? $"{c.GymName} (custom gym)"
        : onMap ? c.GymName
        : c.CityName is { } city ? $"{c.GymName} · {city}" : c.GymName;
}

public sealed class FriendGroupViewModel(FriendGroup g, DateOnly today, bool byGym)
{
    public string Title => g.Title;
    public FriendGroupKind Kind => g.Kind;

    /// <summary>Right side of the header: the count (Recent) or the distance (By gym).</summary>
    public string Meta => g.Kind switch
    {
        FriendGroupKind.Gym when g.DistanceKm is { } km => km < 1 ? $"{Math.Round(km * 1000)} m" : $"{km:0.0} km",
        FriendGroupKind.City => "other city",
        _ => g.Rows.Count.ToString()
    };

    public IReadOnlyList<FriendRowViewModel> Rows { get; } =
        g.Rows.Select(r => new FriendRowViewModel(r, today, g.OnMap, byGym)).ToList();
}
```

### 4b. `ViewModels/GymsViewModel.cs`: changes

The gym list state (search, sort, gyms, custom gyms) moves into the picker dialog's ViewModel, or stays here and is bound by the dialog. Either works; keeping it here is less code. Add the following:

```csharp
// --- Friends map ---
[ObservableProperty] private MapRange _range = MapRange.Month;
[ObservableProperty] private MapMode _mode = MapMode.Latest;
[ObservableProperty] private FriendSort _friendSort = FriendSort.Recent;
[ObservableProperty] private int? _selectedGymId;            // pin, gym dot or friend row clicked
[ObservableProperty] private string _friendCountText = "";   // "3 friends · last 30 days"

public ObservableCollection<GymPin> Pins { get; } = new();
public ObservableCollection<FriendGroupViewModel> FriendGroups { get; } = new();
public ObservableCollection<FriendRowViewModel> SelectedGymVisitors { get; } = new();

/// <summary>Raised after Pins changes so the view can rebuild the Mapsui layer.</summary>
public event EventHandler? PinsChanged;

private IReadOnlyList<FriendCheckIn> _latestPerFriend = [];

// --- Settings chips ---
[ObservableProperty] private bool _shareCheckIns;
[ObservableProperty] private bool _askForGym;

// --- Your gym card ---
[ObservableProperty] private GymListItemViewModel? _homeGym;  // whatever your gym row VM is called
[ObservableProperty] private GymListItemViewModel? _todayGym;
public string TodayText => TodayGym is null ? "Not logged yet" : $"{TodayGym.Name} ✓";
public string TodayButtonText => TodayGym is null ? "Trained elsewhere?" : "Change";

// --- Picker dialog ---
public enum PickerMode { HomeGym, Today }
[ObservableProperty] private PickerMode? _pickerOpenFor;     // null = closed

partial void OnRangeChanged(MapRange value) => _ = LoadFriendMapAsync();
partial void OnModeChanged(MapMode value) => _ = LoadFriendMapAsync();
partial void OnFriendSortChanged(FriendSort value) => RebuildFriendGroups(); // no DB call

partial void OnShareCheckInsChanged(bool value) =>
    _ = _profileService.SetShareGymCheckInsAsync(_session.UserId, value);

partial void OnAskForGymChanged(bool value) =>
    _ = _profileService.SetAskForGymAfterCheckInAsync(_session.UserId, value); // existing setting

partial void OnTodayGymChanged(GymListItemViewModel? value)
{
    OnPropertyChanged(nameof(TodayText));
    OnPropertyChanged(nameof(TodayButtonText));
}

partial void OnSelectedGymIdChanged(int? value)
{
    SelectedGymVisitors.Clear();
    var pin = Pins.FirstOrDefault(p => p.GymId == value);
    if (pin is null) return;
    foreach (var v in pin.Visitors)
        SelectedGymVisitors.Add(new FriendRowViewModel(v, _today, onMap: true, byGym: false));
}

private async Task LoadFriendMapAsync()
{
    var result = await _friendMap.GetAsync(
        _session.UserId, _homeCityId, HomeGym?.Id, _distanceOrigin,
        _today, Range, Mode, FriendSort);

    Pins.Clear();
    foreach (var p in result.Layout.Pins) Pins.Add(p);

    _latestPerFriend = result.Layout.LatestPerFriend;
    SetGroups(result.Groups);

    PinsChanged?.Invoke(this, EventArgs.Empty);
}

private void RebuildFriendGroups() =>
    SetGroups(FriendMapRules.GroupFriends(_latestPerFriend, FriendSort, _homeCityId, HomeGym?.Id, _today));

private void SetGroups(IReadOnlyList<FriendGroup> groups)
{
    FriendGroups.Clear();
    foreach (var g in groups) FriendGroups.Add(new FriendGroupViewModel(g, _today, FriendSort == FriendSort.ByGym));

    var n = _latestPerFriend.Count;
    var range = Range switch { MapRange.Today => "today", MapRange.Week => "last 7 days", _ => "last 30 days" };
    FriendCountText = $"{n} {(n == 1 ? "friend" : "friends")} · {range}";
}

[RelayCommand]
private void ShowFriend(FriendRowViewModel row)
{
    if (!row.CanShow) return;
    SelectedGymId = SelectedGymId == row.GymId ? null : row.GymId; // the view centres the map on it
}

[RelayCommand] private void OpenHomeGymPicker() => PickerOpenFor = PickerMode.HomeGym;
[RelayCommand] private void OpenTodayPicker() => PickerOpenFor = PickerMode.Today;
[RelayCommand] private void ClosePicker() => PickerOpenFor = null;

[RelayCommand]
private async Task ChooseGymAsync(GymListItemViewModel gym)
{
    if (PickerOpenFor == PickerMode.HomeGym)
    {
        await _profileService.SetHomeGymAsync(_session.UserId, gym.Id);
        HomeGym = gym;
        RebuildFriendGroups();                 // "· your gym" label moves
    }
    else
    {
        await _gymService.SetTodaysGymAsync(_session.UserId, gym.Id); // existing "Change gym" call
        TodayGym = gym;
        await LoadFriendMapAsync();            // your own pin moves
    }
    PickerOpenFor = null;
}

[RelayCommand]
private async Task TrainedHereTodayAsync()   // button on the map's gym card
{
    if (SelectedGymId is not { } id) return;
    await _gymService.SetTodaysGymAsync(_session.UserId, id);
    TodayGym = FindGym(id);
    await LoadFriendMapAsync();
}
```

Notes:
- **When to load:** call `LoadFriendMapAsync()` from your existing load method, and after a check-in, Undo, or Change gym, in the same places that refresh the monthly summary.
- **Initial values:** `ShareCheckIns` starts from `profile.ShareGymCheckIns == true`, `AskForGym` from the existing setting, and `HomeGym` from `profile.HomeGymId`.
- **`_distanceOrigin`:** the home gym's coordinates if it's a built-in gym, otherwise the home city centre (City already stores it).
- **The three pill toggles** (range, mode, friend sort) are the sliding pill toggle you already use. Bind `SelectedIndex` through a small index-to-enum converter, or expose int properties.
- **"I trained here today" with no check-in yet:** if today isn't logged, this should open the normal check-in popup with that gym preselected, not create a WorkoutLog by itself. That keeps the Phase 11 50% rule.

### 4c. `Map/FriendMapLayer.cs` (new file)

Builds the friends layer on top of the gym markers you already draw. It's written for Mapsui 4.x/5-style APIs. Property names (`Opacity`, `SymbolScale`, `LabelStyle.Offset`) have moved a little between versions, so check them against the version you installed in Phase 9.

```csharp
using GymApp.Core.Social;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;

namespace GymApp.Desktop.Map;

public static class FriendMapLayer
{
    public const string LayerName = "FriendCheckIns";
    public const string GymIdKey = "GymId";

    public static MemoryLayer Create(IEnumerable<GymPin> pins, int? selectedGymId)
    {
        var features = new List<IFeature>();

        foreach (var pin in pins)
        {
            var (x, y) = SphericalMercator.FromLonLat(pin.Longitude, pin.Latitude);
            var feature = new PointFeature(x, y);
            feature[GymIdKey] = pin.GymId;

            var opacity = (float)FriendMapRules.Opacity(pin.NewestDaysAgo);
            var label = FriendMapRules.Initials(pin.Newest.Username)
                        + (pin.MoreCount > 0 ? $" +{pin.MoreCount}" : "");
            var selected = pin.GymId == selectedGymId;

            // Avatar circle, drawn above the gym dot.
            feature.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse,
                SymbolScale = selected ? 0.9 : 0.75,
                Fill = new Brush(Color.FromArgb(255, 30, 30, 30)),
                Outline = new Pen(Color.White, selected ? 3 : 2),
                Offset = new Offset(0, 22),
                Opacity = opacity
            });

            // Initials (and +N) on the circle.
            feature.Styles.Add(new LabelStyle
            {
                Text = label,
                ForeColor = Color.White,
                BackColor = null,
                Font = new Font { Size = 11, Bold = true },
                Offset = new Offset(0, 22),
                Opacity = opacity
            });

            features.Add(feature);
        }

        return new MemoryLayer(LayerName)
        {
            Features = features,
            Style = null,          // per-feature styles only
            IsMapInfoLayer = true  // so clicks report the feature (Mapsui 4); in 5 use the tap event
        };
    }
}
```

Your own pin (accent fill, black outline) is the same code with `Fill = new Brush(accent)`. Add your own latest check-in as an extra `GymPin`, or a separate tiny layer.

In the Gyms view's code-behind, rebuild the layer when `PinsChanged` fires or `SelectedGymId` changes:

```csharp
private void RebuildFriendLayer()
{
    var map = MapControl.Map;
    var old = map.Layers.FirstOrDefault(l => l.Name == FriendMapLayer.LayerName);
    if (old is not null) map.Layers.Remove(old);
    map.Layers.Add(FriendMapLayer.Create(ViewModel.Pins, ViewModel.SelectedGymId)); // added last = on top
    map.RefreshGraphics();
}

private void CenterOn(int gymId)
{
    var pin = ViewModel.Pins.FirstOrDefault(p => p.GymId == gymId);
    if (pin is null) return;
    var (x, y) = SphericalMercator.FromLonLat(pin.Longitude, pin.Latitude);
    MapControl.Map.Navigator.CenterOn(x, y); // Navigator API in Mapsui 4/5
}
```

Clicks: extend the handler you already use for gym markers. If the clicked feature has `GymIdKey`, set `ViewModel.SelectedGymId`. The gym card (name, address, who was there and when, your visits this month, I trained here today) is an Avalonia `Border` overlaid on the map in the same `Grid` cell, shown when `SelectedGymId` is set. That's simpler than drawing it inside Mapsui. The legend (top-left), the "Today logged at …" toast (top-centre) and the OSM credit (bottom-right) are overlays in the same cell.

Offline: keep the existing "map needs internet" note. The Friends card still works without tiles.

### 4d. `Views/GymsView.axaml`: fit-to-screen layout

The page no longer sits in a ScrollViewer on large windows. Rows are `Auto,*`, the map takes all the height on the left, and the Friends card takes the leftover height on the right and scrolls inside.

```xml
<Grid x:Name="Page" RowDefinitions="Auto,*" Margin="32,24" RowSpacing="16">

  <!-- Header: title + city on the left, two setting chips on the right -->
  <Grid Grid.Row="0" ColumnDefinitions="*,Auto">
    <StackPanel Orientation="Horizontal" Spacing="16" VerticalAlignment="Center">
      <TextBlock Classes="h1" Text="Gyms" />
      <TextBlock Text="{Binding HomeCityName}" /> <!-- + Change city link -->
    </StackPanel>
    <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="8">
      <ToggleButton Classes="settingChip" IsChecked="{Binding ShareCheckIns}"
                    ToolTip.Tip="Friends see the gym and day of your check-ins from the last 30 days">
        <!-- icon + "Visible to friends" + state text + mini switch (ToggleSwitch with IsHitTestVisible=False, or two Borders) -->
      </ToggleButton>
      <ToggleButton Classes="settingChip" IsChecked="{Binding AskForGym}">
        <!-- icon + "Ask for gym" + state text + mini switch -->
      </ToggleButton>
    </StackPanel>
  </Grid>

  <Grid x:Name="Columns" Grid.Row="1" ColumnDefinitions="1.6*,20,*">

    <!-- LEFT: friends map (title row + pills, then the map fills the rest) -->
    <Border Grid.Column="0" Classes="card">
      <Grid RowDefinitions="Auto,*,Auto" RowSpacing="12">
        <!-- Row 0: "Friends map" + range pill (Today/7 days/30 days) + mode pill (Latest/All visits) -->
        <Grid Grid.Row="1" ClipToBounds="True">
          <mapsui:MapControl x:Name="MapControl" />
          <!-- overlays: legend, toast, gym card, empty state, OSM credit -->
        </Grid>
        <TextBlock Grid.Row="2" Classes="hint"
                   Text="Friends only see the gym and the day of a check-in, never the time. Custom gyms show by name only." />
      </Grid>
    </Border>

    <!-- RIGHT: Your gym / Friends / Your check-ins -->
    <Grid Grid.Column="2" RowDefinitions="Auto,*,Auto" RowSpacing="16">

      <Border Grid.Row="0" Classes="card">
        <!-- Your gym: name + address + Change (OpenHomeGymPickerCommand)
             Today: TodayText + TodayButtonText button (OpenTodayPickerCommand) -->
      </Border>

      <Border Grid.Row="1" Classes="card">
        <Grid RowDefinitions="Auto,*" RowSpacing="10">
          <!-- Row 0: "Friends" + FriendCountText, sort pill (Recent / By gym) -->
          <ScrollViewer Grid.Row="1" HorizontalScrollBarVisibility="Disabled">
            <ItemsControl ItemsSource="{Binding FriendGroups}">
              <ItemsControl.ItemTemplate>
                <DataTemplate x:DataType="vm:FriendGroupViewModel">
                  <StackPanel Spacing="2" Margin="0,0,0,8">
                    <!-- group header: dot + Title (uppercase, small) + Meta on the right -->
                    <ItemsControl ItemsSource="{Binding Rows}">
                      <!-- row: Button Command="{Binding $parent[UserControl].((vm:GymsViewModel)DataContext).ShowFriendCommand}"
                           CommandParameter="{Binding}" IsEnabled="{Binding CanShow}"
                           avatar (Opacity) + Username + Subtitle + day badge (accent when IsToday) -->
                    </ItemsControl>
                  </StackPanel>
                </DataTemplate>
              </ItemsControl.ItemTemplate>
            </ItemsControl>
          </ScrollViewer>
        </Grid>
      </Border>

      <Border Grid.Row="2" Classes="card">
        <!-- Your check-ins: title + month arrows, 4 tiles (Workouts, At a gym, No gym, Gyms), one line per gym with a thin bar -->
      </Border>
    </Grid>
  </Grid>

  <!-- Gym picker dialog overlay, spans the whole page -->
  <views:GymPickerDialog Grid.RowSpan="2"
                         IsVisible="{Binding PickerOpenFor, Converter={x:Static ObjectConverters.IsNotNull}}" />
</Grid>
```

- **Inner scrolling:** the Friends card's `ScrollViewer` sits in a `*` row, so it gets exactly the leftover height and scrolls by itself. Use `ItemsControl`, not `ListBox`, inside it, since a `ListBox` brings its own ScrollViewer.
- **Small windows:** below about 1100 × 700, wrap `Page` in a `ScrollViewer` and give the map row a fixed height (520). Below 1100 wide, also switch `Columns` to one column, with the map first and the right-hand cards underneath. Reuse the same approach as the Home dashboard from 12c (Classes toggled from `Bounds`), and add the height check.
- **Selected row:** when `SelectedGymId == row.GymId`, outline the row with a white border. A `MultiBinding` or a bool on the row VM refreshed in `OnSelectedGymIdChanged` both work.

### 4e. `Views/GymPickerDialog.axaml` (new)

The old picker card moves here unchanged in behaviour: search, A–Z / Nearest pill, refresh, the gym list, and Add custom gym. Make it an overlay `UserControl`: a semi-transparent `Border` filling the page with a centred card, max 520 × 680. That way it works the same on every platform. A separate `Window` shown with `ShowDialog` also works if you prefer.

- **Title and subtitle by mode:**
  - HomeGym: "Choose your gym" / "Your gym is preselected after every check-in. You only set it once."
  - Today: "Where did you train today?" / "Only today's log changes. Your gym stays {HomeGym.Name}."
- **Row button by mode:** HomeGym shows Set / Current, Today shows Pick / Picked. It calls `ChooseGymCommand` with the row.
- **Badges:** "Your gym", "Custom", and "N friends", where N is the friends whose latest check-in is there, from `Pins`.
- **Gym list:** a `ScrollViewer` in a `*` row, the same as the Friends card.
- **Closing:** Close (X), Cancel, and the Escape key all run `ClosePicker`. Handle Escape in the dialog's `KeyDown`, not globally. Focus the search box when it opens.

### 4f. Check-in flow: preselect your gym, ask about sharing once

In the gym picker prompt after "I went today":
- With **Ask for gym** on, open the prompt with `HomeGymId` preselected, so one tap confirms it.
- With it off, there's no prompt. The service sets the home gym (3e). With no home gym set, it behaves as before and logs no gym.

After a gym is picked, if `profile.ShareGymCheckIns is null`, show one extra line in the same popup:

> **Show this check-in to friends?** Friends see the gym and the day, never the time. You can change this on the Gyms screen. [Share] [Not now]

Share sets `true`, Not now sets `false`. Either way, it's never asked again.

If the user has no home gym yet, add a checkbox under the gym list: "Make this my gym". It's checked by default the first time, and it calls `SetHomeGymAsync`.

---

## 5. Tests (GymApp.Tests)

### `FriendMapRulesTests.cs`

- **`WindowStart`:** today minus 29 days. A check-in 29 days ago is in, 30 days ago is out.
- **`InRange`:** Today keeps only today, and 7 days keeps 0 to 6 days ago. A future date is never in range.
- **`Select` with `Latest`:** one entry per friend, the newest one. Sorting stays stable by username.
- **`Select` with `AllVisits`:** one entry per friend per gym. Three visits to the same gym become one, with the newest date.
- **`Layout`:**
  - Home-city gyms with coordinates become pins.
  - Two friends at one gym become one pin, with `MoreCount == 1` and the newest first.
  - Another city's gym and a custom gym never become pins.
  - `LatestPerFriend` is the same in both modes.
- **`GroupFriends` with `Recent`:**
  - Today, This week and Earlier this month split at 0, 1–6 and 7+ days.
  - Other cities and custom gyms go to Elsewhere, with `OnMap == false`.
  - Empty groups are left out.
  - Rows are newest first.
- **`GroupFriends` with `ByGym`:**
  - One group per gym, ordered by `DistanceKm`, with unknown distances last and then by name.
  - The home gym's title ends with "· your gym".
  - Other cities come last, one group per city, sorted by name.
  - Rows are newest first.
- **`Opacity` buckets** at 0, 1, 2, 3, 7, 8 and 29 days.
- **`DescribeDay`:** Today, Yesterday, 2 to 6 days ago, then the date.
- **`Initials`:** `andrei.m` → `AN`, `vlad_t` → `VL`, and a username of digits only → `?`.
- **`DistanceKm`:** 0 for the same point, and about 111 km for 1° of latitude.

```csharp
[Fact]
public void Two_friends_at_one_gym_share_a_pin_newest_first()
{
    var today = new DateOnly(2026, 10, 19);
    var all = new[]
    {
        CheckIn(1, "andrei.m", gymId: 10, today),
        CheckIn(2, "maria_l", gymId: 10, today.AddDays(-3)),
    };

    var layout = FriendMapRules.Layout(all, homeCityId: 1, today, MapRange.Week, MapMode.Latest);

    var pin = Assert.Single(layout.Pins);
    Assert.Equal("andrei.m", pin.Newest.Username);
    Assert.Equal(1, pin.MoreCount);
    Assert.Equal(0, pin.NewestDaysAgo);
}

[Fact]
public void By_gym_orders_nearest_first_and_puts_other_cities_last()
{
    var today = new DateOnly(2026, 10, 19);
    var latest = new[]
    {
        CheckIn(1, "andrei.m", gymId: 10, today, km: 1.1),
        CheckIn(2, "vlad_t", gymId: 11, today.AddDays(-1), km: 0.5),
        CheckIn(3, "maria_l", gymId: 99, today.AddDays(-4), cityId: 2, city: "Cluj-Napoca"),
    };

    var groups = FriendMapRules.GroupFriends(latest, FriendSort.ByGym, homeCityId: 1, homeGymId: 10, today);

    Assert.Equal(new[] { "Gym 11", "Gym 10 · your gym", "Cluj-Napoca" }, groups.Select(g => g.Title));
    Assert.False(groups[^1].OnMap);
}

private static FriendCheckIn CheckIn(int id, string name, int gymId, DateOnly date,
    int cityId = 1, string city = "Timișoara", bool custom = false, double? km = null) =>
    new(id, name, gymId, $"Gym {gymId}", cityId, city,
        custom ? null : 45.75, custom ? null : 21.23, km, custom, date);
```

### `FriendMapServiceTests.cs` (in-memory SQLite with the real migrations, like the other service tests)

- An accepted friend with `ShareGymCheckIns = true` appears; `false` and `null` don't.
- A pending friend request doesn't share anything.
- A log without a gym doesn't appear.
- Turning sharing off hides past check-ins on the next load.
- Undoing today's check-in removes it from the next load.
- A custom gym comes back with null coordinates.
- With an origin set, `DistanceKm` is filled in for gyms with coordinates and stays null for custom gyms.

### `HomeGymTests.cs`

- `SetHomeGymAsync` accepts a built-in gym and the user's own custom gym.
- `SetHomeGymAsync` rejects another user's custom gym and an inactive gym.
- Deleting the custom gym that is someone's home gym sets `HomeGymId` to null (SetNull), and nothing else is deleted.
- With Ask for gym off and a home gym set, a check-in gets that `GymId`. With no home gym, `GymId` stays null.
- Changing today's gym ("Trained elsewhere?") never changes `HomeGymId`.

---

## 6. Docs to update

- **Phase 10 / implementation plan, privacy line:** "Only public stats are shared: username, level, points, and streak. Gym check-ins (gym and day, last 30 days) are shared only when the user turns on Visible to friends. Body data, calories, sets and PRs stay private."
- **Implementation plan:**
  - Add this as a 1.1.0 feature next to the optional extras.
  - Add a row for `UserProfile.ShareGymCheckIns` and `HomeGymId` to the data model appendix (Phase 1.1).
  - Phase 9's "Gym picker after check-in" now opens with your gym preselected.
- **Gyms screen footnote:** the old "Only you see your gyms. They are never shared with friends." is replaced by the line under the map and the Visible to friends chip.

## 7. When the sync server (10f) exists

- The server stores only check-ins of users who share, and only the last 30 days. A daily job deletes older ones there; that's the place for real deletion, not the local database.
- The payload is gym id, gym name, city, coordinates (built-in gyms only) and the date. Never a time, never a custom gym's address, and never anyone's home gym.
- Turning sharing off deletes that user's check-ins from the server, not only hides them.
- Distances for By gym are computed on the client from the user's own origin, so nobody's location is sent to the server for it.
