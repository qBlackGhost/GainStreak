using System.Globalization;

namespace GymApp.Core;

/// <summary>How far back the friends map looks: today, the last 7 days or the last 30.</summary>
public enum MapRange { Today, Week, Month }

/// <summary>Latest = one pin per friend (their newest check-in); AllVisits = one per friend per gym.</summary>
public enum MapMode { Latest, AllVisits }

/// <summary>How the Friends card groups its rows.</summary>
public enum FriendSort { Recent, ByGym }

/// <summary>One friend check-in at a gym, as friends may see it: gym and day, never a time.
/// Coordinates are null for custom gyms and gyms without a position.</summary>
public sealed record FriendCheckIn(
    int FriendUserId,
    string Username,
    int GymId,
    string GymName,
    int? CityId,
    string? CityName,
    double? Latitude,
    double? Longitude,
    double? DistanceKm,          // from the user's own origin (home gym or home city centre), null if unknown
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

/// <summary>A section of the Friends card. <see cref="OnMap"/> false (Elsewhere, another city): rows can't be clicked.</summary>
public sealed record FriendGroup(
    FriendGroupKind Kind,
    string Title,               // "Today", "World Class", "Cluj-Napoca", ...
    int? GymId,                 // set for Kind == Gym
    double? DistanceKm,         // set for Kind == Gym
    IReadOnlyList<FriendCheckIn> Rows,
    bool OnMap);

/// <summary>Everything the Map screen needs for the friends layer: the pins, and each friend's newest check-in
/// for the Friends card.</summary>
public sealed record FriendMapLayout(
    IReadOnlyList<GymPin> Pins,
    IReadOnlyList<FriendCheckIn> LatestPerFriend);

/// <summary>
/// The friends map's rules, kept here so they're unit-testable: the 30-day window, range and mode, pins, the recency
/// fade, and the Friends card's two groupings. Privacy rules live here too: custom gyms and gyms in other cities never
/// become pins, and only a day is ever described, never a time.
/// </summary>
public static class FriendMapRules
{
    public const int WindowDays = 30;

    private static readonly StringComparer NameOrder = StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.IgnoreCase);

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

    /// <summary>Inside the range, counting today as day 1. Future dates never are.</summary>
    public static bool InRange(DateOnly date, DateOnly today, MapRange range)
    {
        var d = DaysAgo(date, today);
        return d >= 0 && d < RangeDays(range);
    }

    /// <summary>Recency fade for pins and avatars: today 100%, 1–2 days 85%, 3–7 days 60%, older 35%.</summary>
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

    /// <summary>A check-in can be a pin when it's a built-in gym with coordinates in the user's home city.</summary>
    public static bool IsOnMap(FriendCheckIn c, int? homeCityId) =>
        !c.IsCustomGym && c.Latitude is not null && c.Longitude is not null &&
        homeCityId is not null && c.CityId == homeCityId;

    private static IOrderedEnumerable<FriendCheckIn> NewestFirst(IEnumerable<FriendCheckIn> rows) =>
        rows.OrderByDescending(c => c.Date).ThenBy(c => c.Username, NameOrder).ThenBy(c => c.FriendUserId);

    /// <summary>
    /// Applies range and mode. Latest = one entry per friend (their newest check-in). AllVisits = one entry per friend
    /// per gym (the newest visit there), so a pin never shows the same friend twice. Newest first.
    /// </summary>
    public static IReadOnlyList<FriendCheckIn> Select(
        IEnumerable<FriendCheckIn> all, DateOnly today, MapRange range, MapMode mode)
    {
        var inRange = all.Where(c => InRange(c.Date, today, range));
        var picked = mode == MapMode.Latest
            ? inRange.GroupBy(c => c.FriendUserId).Select(g => NewestFirst(g).First())
            : inRange.GroupBy(c => (c.FriendUserId, c.GymId)).Select(g => NewestFirst(g).First());
        return NewestFirst(picked).ToList();
    }

    /// <summary>Pins for the map (newest first) plus each friend's newest check-in for the Friends card. The card uses
    /// the newest check-in in either map mode.</summary>
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
                var visitors = NewestFirst(g).ToList();
                var first = visitors[0];
                return new GymPin(first.GymId, first.GymName, first.Latitude!.Value, first.Longitude!.Value,
                    visitors, DaysAgo(first.Date, today));
            })
            .OrderBy(p => p.NewestDaysAgo)
            .ThenBy(p => p.GymName, NameOrder)
            .ToList();

        var latest = mode == MapMode.Latest ? selected : Select(list, today, range, MapMode.Latest);
        return new FriendMapLayout(pins, latest);
    }

    /// <summary>
    /// Sections of the Friends card for the chosen sort; empty sections are left out, rows are newest first.
    /// Recent: Today / This week / Earlier this month for check-ins on the map, then Elsewhere (other cities, custom gyms).
    /// ByGym: one group per gym, nearest first (unknown distance last, then by name), your gym marked; then one group per
    /// other city, by name.
    /// </summary>
    public static IReadOnlyList<FriendGroup> GroupFriends(
        IReadOnlyList<FriendCheckIn> latestPerFriend, FriendSort sort,
        int? homeCityId, int? homeGymId, DateOnly today)
    {
        var onMap = latestPerFriend.Where(c => IsOnMap(c, homeCityId)).ToList();
        var away = latestPerFriend.Where(c => !IsOnMap(c, homeCityId)).ToList();
        var groups = new List<FriendGroup>();

        if (sort == FriendSort.Recent)
        {
            void Add(FriendGroupKind kind, string title, IEnumerable<FriendCheckIn> rows, bool onTheMap)
            {
                var list = NewestFirst(rows).ToList();
                if (list.Count > 0) groups.Add(new FriendGroup(kind, title, null, null, list, onTheMap));
            }

            Add(FriendGroupKind.Today, "Today", onMap.Where(c => DaysAgo(c.Date, today) == 0), true);
            Add(FriendGroupKind.ThisWeek, "This week", onMap.Where(c => DaysAgo(c.Date, today) is >= 1 and < 7), true);
            Add(FriendGroupKind.Earlier, "Earlier this month", onMap.Where(c => DaysAgo(c.Date, today) >= 7), true);
            Add(FriendGroupKind.Elsewhere, "Elsewhere", away, false);
            return groups;
        }

        groups.AddRange(onMap
            .GroupBy(c => c.GymId)
            .Select(g => new { First = g.First(), Rows = NewestFirst(g).ToList() })
            .OrderBy(x => x.First.DistanceKm ?? double.MaxValue)
            .ThenBy(x => x.First.GymName, NameOrder)
            .Select(x => new FriendGroup(
                FriendGroupKind.Gym,
                x.First.GymId == homeGymId ? $"{x.First.GymName} · your gym" : x.First.GymName,
                x.First.GymId, x.First.DistanceKm, x.Rows, true)));

        groups.AddRange(away
            .GroupBy(c => c.CityName ?? "Other places")
            .OrderBy(g => g.Key, NameOrder)
            .Select(g => new FriendGroup(FriendGroupKind.City, g.Key, null, null, NewestFirst(g).ToList(), false)));

        return groups;
    }

    /// <summary>The gym picker's "N friends" badges: friends whose newest check-in is at each gym. Only gyms on this phone
    /// (positive ids); a friend's gym the phone doesn't know has a negative id and no row to badge.</summary>
    public static IReadOnlyDictionary<int, int> FriendsAtGym(IEnumerable<FriendCheckIn> latestPerFriend) =>
        latestPerFriend.Where(c => c.GymId > 0).GroupBy(c => c.GymId).ToDictionary(g => g.Key, g => g.Count());

    /// <summary>A friend's pin letters: <see cref="UserInitials"/>.</summary>
    public static string Initials(string username) => UserInitials.Of(username);

    /// <summary>Straight-line distance in km, for "By gym" ordering.</summary>
    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2) =>
        Geo.HaversineKm(lat1, lon1, lat2, lon2);
}
