namespace GymApp.Core;

/// <summary>The groups of the badge screen. Hidden badges have their own group.</summary>
public enum BadgeGroup { Consistency, Strength, BodyAndHealth, Social, Hidden }

/// <summary>What a badge is drawn in. Standard badges take their tier's material (Steel, Lime, Fire, Blaze; Lime for a
/// one-time badge); Ice and Limited are fixed materials (On Ice, Monthly Challenge).</summary>
public enum BadgeMaterial { Standard, Ice, Limited }

/// <summary>
/// One badge (Phase 25, docs/PHASE_25_BADGES.md). <see cref="Tiers"/> holds the thresholds for Steel, Lime, Fire and
/// Blaze; empty means a one-time badge. Each badge is earned once: something you can do again climbs tiers instead.
/// </summary>
/// <param name="Id">Stable kebab-case id, sent over the wire. Never rename one.</param>
/// <param name="Hint">The one line a hidden badge shows before it is earned; null for visible badges.</param>
/// <param name="Glyph">The glyph key from the spec's drawing appendix, or null when <see cref="Text"/> is drawn.</param>
/// <param name="TierGlyphs">A different glyph per tier (Heavy Lifter's car, elephant, whale, station), or null.</param>
/// <param name="TierNames">Names for the tiers instead of Steel..Blaze in the copy (Heavy Lifter), or null.</param>
public sealed record BadgeDefinition(
    string Id,
    string Name,
    BadgeGroup Group,
    string Rule,
    IReadOnlyList<double> Tiers,
    string? Glyph,
    string? Text = null,
    string? Hint = null,
    BadgeMaterial Material = BadgeMaterial.Standard,
    IReadOnlyList<string>? TierGlyphs = null,
    IReadOnlyList<string>? TierNames = null)
{
    public bool Hidden => Group == BadgeGroup.Hidden;
    public bool IsTiered => Tiers.Count > 0;
}

/// <summary>Every badge except the monthly ones, which <see cref="MonthlyChallenges"/> makes per month.</summary>
public static class BadgeCatalog
{
    public const int MaxTier = 4;
    public static readonly IReadOnlyList<string> TierMaterialNames = ["Steel", "Lime", "Fire", "Blaze"];

    private static readonly double[] Once = [];

    public static readonly IReadOnlyList<BadgeDefinition> All =
    [
        // Consistency
        new("streak", "Streak", BadgeGroup.Consistency, "Keep your streak going.", [7, 30, 100, 365], "flame"),
        new("perfect-week", "Perfect Week", BadgeGroup.Consistency, "Do every workout you planned for the week.", [1, 4, 12, 52], "week"),
        new("on-ice", "On Ice", BadgeGroup.Consistency, "A rest day keeps your streak alive instead of breaking it.", Once, "snow",
            Material: BadgeMaterial.Ice),
        new("weekend-warrior", "Weekend Warrior", BadgeGroup.Consistency, "Work out on both the Saturday and the Sunday of a weekend.",
            [1, 5, 15, 52], "weekend"),
        new("split-finisher", "Split Finisher", BadgeGroup.Consistency, "Do every day of your split once, in any order.",
            [1, 5, 20, 50], "grid"),

        // Strength
        new("pr-breaker", "PR Breaker", BadgeGroup.Strength, "Set new personal records.", [1, 10, 50, 100], "bolt"),
        new("heavy-lifter", "Heavy Lifter", BadgeGroup.Strength, "Lift a total volume over your lifetime.",
            [1_500, 6_000, 150_000, 420_000], "car",
            TierGlyphs: ["car", "elephant", "whale", "station"], TierNames: ["Car", "Elephant", "Blue whale", "Space station"]),
        new("century", "Century", BadgeGroup.Strength, "Log workouts.", [10, 50, 100, 500], null, Text: "100"),
        new("bodyweight-club", "Bodyweight Club", BadgeGroup.Strength, "Lift a multiple of your body weight in one set.",
            [1, 1.5, 2, 2.5], "bodyweight"),
        new("rep-machine", "Rep Machine", BadgeGroup.Strength, "Lifetime reps.", [1_000, 10_000, 50_000, 100_000], "reps"),
        new("exercise-explorer", "Exercise Explorer", BadgeGroup.Strength, "Log different exercises.", [10, 25, 50, 100], "compass"),
        new("pr-week", "PR Week", BadgeGroup.Strength, "Set 3 or more personal records in one week.", [1, 3, 10, 25], null, Text: "×3"),

        // Body & health
        new("fuel-streak", "Fuel Streak", BadgeGroup.BodyAndHealth, "Stay within your calorie goal, days in a row.",
            [7, 14, 30, 90], "fuel"),
        new("weigh-in-habit", "Weigh-in Habit", BadgeGroup.BodyAndHealth, "Log your weight every week, weeks in a row.",
            [4, 12, 26, 52], "scale"),
        new("cardio-minutes", "Cardio Minutes", BadgeGroup.BodyAndHealth, "Lifetime cardio minutes.", [60, 600, 3_000, 10_000], "cardio"),

        // Social & gyms (Monthly Challenge comes from MonthlyChallenges)
        new("regular", "Regular", BadgeGroup.Social, "Work out 25 times at the same gym.", Once, "pin"),
        new("explorer", "Explorer", BadgeGroup.Social, "Train at 5 different gyms.", Once, "map"),
        new("we-go-jim", "We Go Jim", BadgeGroup.Social, "Work out at the same gym, on the same day, as a friend.",
            [1, 5, 25, 100], "friends"),
        new("recruiter", "Recruiter", BadgeGroup.Social, "Someone becomes your friend through your QR code or invite link.", Once,
            "recruiter"),
        new("top-of-the-board", "Top of the Board", BadgeGroup.Social,
            "Finish a week first on your friends' weekly leaderboard.", [1, 5, 10, 25], "trophy"),

        // Hidden
        new("ill-be-back", "I'll Be Back", BadgeGroup.Hidden, "Log a workout after 14 or more days off.", Once, "comeback",
            Hint: "Everyone has a slow patch."),
        new("night-owl", "Night Owl", BadgeGroup.Hidden, "Start a live workout between 23:00 and 03:59.", Once, "moon",
            Hint: "Trained while the city slept."),
        new("eye-of-the-tiger", "Eye of the Tiger", BadgeGroup.Hidden, "Start a live workout between 04:00 and 05:59.", Once,
            "sunrise", Hint: "Some people train before sunrise."),
        new("double-up", "Double Up", BadgeGroup.Hidden, "Do a strength workout and a cardio session on the same day.", Once,
            "double", Hint: "One workout wasn't enough today."),
        new("marathon-session", "Marathon Session", BadgeGroup.Hidden, "Do one live workout of 2 hours or more.", Once, "clock",
            Hint: "Lose track of time at the gym."),
        new("triple-digits", "Triple Digits", BadgeGroup.Hidden, "Lift 100 kg or more in one set.", Once, "plate",
            Hint: "A big round number on the bar."),
        new("fresh-start", "Fresh Start", BadgeGroup.Hidden, "Log a workout on January 1.", Once, "sparkle",
            Hint: "Start the year the right way."),
        new("first-of-month", "First of the Month!", BadgeGroup.Hidden,
            "Log workouts on the 1st of the month, February to December.", [1, 3, 6, 11], "month1", Hint: "New month, same habit."),
        new("birthday-gains", "Birthday Gains", BadgeGroup.Hidden, "Log a workout on your birthday.", Once, "cake",
            Hint: "Make a wish, then lift."),
        new("leap-day", "Leap Day", BadgeGroup.Hidden, "Log a workout on February 29.", Once, null, Text: "29",
            Hint: "Rarer than a PR."),
        new("christmas-gains", "Christmas Gains", BadgeGroup.Hidden, "Log a workout on December 25.", Once, "tree",
            Hint: "Some gifts come in plates."),
        new("full-house", "Full House", BadgeGroup.Hidden, "Log every exercise of the day's plan, 10 times.", Once, "checklist",
            Hint: "Nothing left on the list."),
        new("rest-day-never", "Rest Day? Never Heard of Her", BadgeGroup.Hidden, "Work out 14 days in a row.", Once, null,
            Text: "14", Hint: "Rest days are for other people."),
        new("this-is-fine", "This Is Fine", BadgeGroup.Hidden,
            "Miss a training day, make it up the next day, and keep the streak.", Once, "mug", Hint: "Everything is under control."),
        new("touch-grass", "Touch Grass", BadgeGroup.Hidden, "Take a vacation, then train within 3 days of coming back.", Once,
            "grass", Hint: "Even lifters need a break."),
        new("completion", "100% Completion", BadgeGroup.Hidden, "Earn every visible badge at least once.", Once, null,
            Text: "100%", Hint: "Collect every one in sight."),
        new("light-weight-baby", "Light Weight, Baby!", BadgeGroup.Hidden, "Lift 150 kg or more in one set.", Once, null,
            Text: "150", Hint: "Yeah buddy!"),
        new("never-skip-leg-day", "Never Skip Leg Day", BadgeGroup.Hidden, "Train legs every week, weeks in a row.",
            [4, 8, 26, 52], "leg", Hint: "Chicken legs need not apply."),
    ];

    private static readonly Dictionary<string, BadgeDefinition> ById = All.ToDictionary(b => b.Id);

    /// <summary>The badge with this id, a monthly one included, or null for an id this version doesn't know (a badge
    /// from a newer app, which the screens skip).</summary>
    public static BadgeDefinition? Find(string id) =>
        ById.TryGetValue(id, out var b) ? b : MonthlyChallenges.Definition(id);

    /// <summary>The visible badges 100% Completion asks for: every visible badge except the monthly ones.</summary>
    public static IEnumerable<BadgeDefinition> CompletionSet => All.Where(b => !b.Hidden);

    /// <summary>"Steel", "Lime", "Fire" or "Blaze" for tier 1 to 4, or the badge's own tier name (Heavy Lifter's
    /// "Elephant"); null for no tier.</summary>
    public static string? TierName(BadgeDefinition badge, int tier) =>
        tier is < 1 or > MaxTier ? null
        : badge.TierNames is { } names ? names[tier - 1]
        : TierMaterialNames[tier - 1];

    /// <summary>The glyph to draw at <paramref name="tier"/> (Heavy Lifter changes with its level).</summary>
    public static string? GlyphFor(BadgeDefinition badge, int tier) =>
        badge.TierGlyphs is { } glyphs ? glyphs[Math.Clamp(tier, 1, glyphs.Count) - 1] : badge.Glyph;
}
