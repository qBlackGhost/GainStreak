namespace GymApp.Core;

/// <summary>One workout log. <paramref name="GymKey"/> names its gym: <c>osm:&lt;type&gt;:&lt;id&gt;</c> for an OpenStreetMap gym
/// (see <see cref="BadgeInputs.OsmGymKey"/>), <c>custom:&lt;id&gt;</c> for a custom one, null for none. Only OSM gyms can
/// match a friend's check-in.</summary>
public sealed record BadgeWorkout(
    DateOnly Date,
    string? GymKey = null,
    int? SplitId = null,
    int? SplitDayId = null,
    int? FilledExercises = null,
    int? TotalExercises = null,
    int? CardioMinutes = null);

/// <summary>One logged set. <paramref name="IsLegs"/>: the exercise's muscle group is Quads, Hamstrings, Glutes or Calves.</summary>
public sealed record BadgeSet(DateOnly Date, int ExerciseId, int SetNumber, SetKind Kind, int Reps, double WeightKg, bool IsLegs = false);

/// <summary>A day's calories. <paramref name="Goal"/> is the goal that day (<c>CalorieLog.GoalAtTime</c>), null when none was set.</summary>
public sealed record BadgeCalorieDay(DateOnly Date, int? Goal, int Consumed);

/// <param name="FromHealth">Imported from Apple Health (0.27.1): it gives Bodyweight Club the body weight, but only
/// weigh-ins logged in the app build the Weigh-in Habit.</param>
public sealed record BadgeWeighIn(DateOnly Date, double WeightKg, bool FromHealth = false);

/// <summary>A live workout, in local time. <paramref name="AutoStopped"/>: the app ended it, not the user.</summary>
public sealed record BadgeLiveWorkout(DateOnly Date, DateTime StartedLocal, DateTime? EndedLocal, bool AutoStopped);

/// <summary>A saved cardio session. <paramref name="Minutes"/> are added to <see cref="BadgeWorkout.CardioMinutes"/>, so the
/// service passes only the minutes the day's log doesn't already hold (0 when it does): each minute is counted once.
/// The session itself always counts for Double Up.</summary>
public sealed record BadgeCardio(DateOnly Date, int Minutes);

/// <summary>A friend's gym check-in: day and gym, in the same key form as <see cref="BadgeWorkout.GymKey"/>.</summary>
public sealed record BadgeFriendCheckIn(DateOnly Date, string GymKey);

/// <summary>A vacation. <paramref name="ReturnDate"/> is the first normal day again (after an early end, the day after it).</summary>
public sealed record BadgeVacation(DateOnly Start, DateOnly ReturnDate);

/// <summary>A finished week of the friends' leaderboard: whether the user was first (shared first included), with at
/// least one friend who scored that week.</summary>
public sealed record BadgeWeekResult(DateOnly WeekStart, bool First);

/// <summary>
/// Everything <see cref="BadgeEvaluator"/> needs, as plain values (no EF types). Lists may come in any order.
/// </summary>
public sealed record BadgeInputs
{
    public IReadOnlyList<BadgeWorkout> Workouts { get; init; } = [];
    public IReadOnlyList<BadgeSet> Sets { get; init; } = [];

    /// <summary>Manual PR corrections per exercise, so PRs are counted the way the app shows them.</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<PrAdjustment>> PrAdjustments { get; init; } =
        new Dictionary<int, IReadOnlyList<PrAdjustment>>();

    /// <summary>The longest streak (<see cref="StreakResult.Longest"/>).</summary>
    public int LongestStreak { get; init; }

    /// <summary>Every training day from the first workout to today (<see cref="StreakCalculator.Timeline"/>).</summary>
    public IReadOnlyList<TrainingDayOutcome> TrainingDays { get; init; } = [];

    /// <summary>The days the streak was kept on ice (<see cref="StreakCalculator.ColdDays"/>).</summary>
    public IReadOnlyList<DateOnly> ColdDays { get; init; } = [];

    public IReadOnlyList<BadgeCalorieDay> Calories { get; init; } = [];
    public IReadOnlyList<BadgeWeighIn> WeighIns { get; init; } = [];
    public IReadOnlyList<BadgeLiveWorkout> LiveWorkouts { get; init; } = [];
    public IReadOnlyList<BadgeCardio> Cardio { get; init; } = [];
    public IReadOnlyList<BadgeFriendCheckIn> FriendCheckIns { get; init; } = [];

    /// <summary>The day ids of each split, for Split Finisher.</summary>
    public IReadOnlyDictionary<int, IReadOnlySet<int>> SplitDays { get; init; } = new Dictionary<int, IReadOnlySet<int>>();

    public IReadOnlyList<BadgeVacation> Vacations { get; init; } = [];
    public DateOnly? BirthDate { get; init; }

    /// <summary>People who became friends through the user's invite (from the server).</summary>
    public int AcceptedInvites { get; init; }

    public IReadOnlyList<BadgeWeekResult> WeekResults { get; init; } = [];

    /// <summary>Ids of badges already stored as earned. They count for 100% Completion even if today's data no
    /// longer earns them (earned badges are kept).</summary>
    public IReadOnlySet<string> PreviouslyEarned { get; init; } = new HashSet<string>();

    /// <summary>The gym key for an OpenStreetMap gym, e.g. <c>osm:node:123</c>.</summary>
    public static string OsmGymKey(string osmType, long osmId) => $"osm:{osmType.ToLowerInvariant()}:{osmId}";

    /// <summary>The gym key for a custom gym.</summary>
    public static string CustomGymKey(int gymId) => $"custom:{gymId}";
}
