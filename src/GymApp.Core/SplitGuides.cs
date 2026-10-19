namespace GymApp.Core;

/// <summary>What the split detail screen says about a preset: a short "about" line and who it suits.</summary>
public record SplitGuide(string About, IReadOnlyList<string> GoodFor);

/// <summary>Descriptions for the built-in presets, keyed by their seeded ids (AppDbContext). Custom splits have none.</summary>
public static class SplitGuides
{
    private static readonly IReadOnlyDictionary<int, SplitGuide> ByPresetId = new Dictionary<int, SplitGuide>
    {
        [1] = new("Each muscle group is trained twice a week across three focused days, with one rest day in the middle.",
            ["Training 6 days a week", "Building muscle with higher volume", "Short, focused sessions"]),
        [2] = new("Alternates an upper-body day and a lower-body day, so each half of the body is trained twice a week in four sessions.",
            ["Training 4 days a week", "Balancing strength and recovery", "Beginners moving past full body"]),
        [3] = new("Three focused PPL days, a rest day, then an upper and a lower day, so each muscle is hit about twice a week in five sessions.",
            ["Training 5 days a week", "Mixing focus days with full upper / lower days", "Intermediate lifters"]),
        [4] = new("Every session trains the whole body, with a rest day or more between sessions.",
            ["Beginners", "Busy weeks with 2–3 sessions", "Learning the main lifts"]),
        [5] = new("One muscle group per day, five days in a row, so each gets a full session once a week.",
            ["Training 5 days a week", "Lots of volume per muscle in one session", "Lifters who like one focus per day"]),
    };

    public static SplitGuide? For(int splitId, bool isPreset) =>
        isPreset && ByPresetId.TryGetValue(splitId, out var g) ? g : null;
}
