using System;
using System.Linq;

namespace GymApp.Core;

/// <summary>
/// Weights entered per side of a bar. The sets always store the total (bar + both sides), so PRs, points and
/// stats never need to know how the weight was typed.
/// </summary>
public static class BarWeight
{
    // Anything with these is loaded on a bar (Smith machines included: their bar weight varies, so it is optional)
    private static readonly string[] BarWords =
    [
        "barbell", "bench press", "deadlift", "squat", "row", "overhead press", "military press", "curl",
        "skull crusher", "good morning", "hip thrust", "smith", "ez bar", "ez-bar", "shrug"
    ];

    // ...unless it is done with something else
    private static readonly string[] NotBarWords =
    [
        "dumbbell", "cable", "machine", "kettlebell", "band", "leg curl", "hammer", "bulgarian", "hack squat",
        "leg press", "lat pulldown", "low row", "wrist", "forearm"
    ];

    /// <summary>The exercise is done with a bar, so the Total / Per side toggle is offered.</summary>
    public static bool UsesBar(string exerciseName)
    {
        var name = exerciseName.ToLowerInvariant();
        return BarWords.Any(name.Contains) && !NotBarWords.Any(name.Contains)
               || name.Contains("smith") || name.Contains("barbell");
    }

    /// <summary>Total on the bar: both sides plus the bar (an empty bar field counts as 0).</summary>
    public static double Total(double perSideKg, double? barKg) => Math.Round(2 * perSideKg + (barKg ?? 0), 2);

    /// <summary>What goes on each side for a total; never below 0 when the bar alone is heavier.</summary>
    public static double PerSide(double totalKg, double? barKg) => Math.Max(0, Math.Round((totalKg - (barKg ?? 0)) / 2, 2));
}
