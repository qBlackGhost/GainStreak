using System.Globalization;

namespace GymApp.Core;

/// <summary>What the user is aiming for. Stored as its name in the database, so don't rename the members.</summary>
public enum GoalType { Lose, Maintain, Gain }

/// <summary>A suggested daily goal. <see cref="Note"/> says when a safety limit changed the number.</summary>
public record CalorieSuggestion(int Kcal, int Maintenance, string? Note = null);

/// <summary>Suggested calorie goals, safety limits, and what "hit the target" means per goal.</summary>
public static class CalorieGoals
{
    // Lose: about 500 kcal under maintenance, kept between 15 and 20 percent of it
    public const int LoseDeficit = 500;
    public const double MinDeficitShare = 0.15;
    public const double MaxDeficitShare = 0.20;

    // Gain: about 10 percent over maintenance, kept between 250 and 300 kcal
    public const double SurplusShare = 0.10;
    public const int MinSurplus = 250;
    public const int MaxSurplus = 300;

    // Safety: never under the floor for your sex, never more than 25 percent under maintenance
    public const int MaleFloor = 1500;
    public const int FemaleFloor = 1200;
    public const double MaxDeficitLimit = 0.25;

    public const int MaxGoal = 6000;
    public const int MaxConsumed = 15000;

    /// <summary>Maintain counts as hit within this share of the goal, either way.</summary>
    public const double MaintainTolerance = 0.075;

    /// <summary>Whole kcal with thousands separators, e.g. 2,260.</summary>
    public static string K(int kcal) => kcal.ToString("N0", CultureInfo.InvariantCulture);

    public static string DisplayName(GoalType goal) => goal switch
    {
        GoalType.Lose => "Lose weight",
        GoalType.Maintain => "Maintain",
        GoalType.Gain => "Gain weight",
        _ => goal.ToString()
    };

    /// <summary>The lowest goal for your sex. Without a sex, the lower (female) floor.</summary>
    public static int Floor(Sex? sex) => sex == Sex.Male ? MaleFloor : FemaleFloor;

    /// <summary>The lowest safe goal: the floor, or 25 percent under maintenance when that is higher.</summary>
    public static int SafeMinimum(Sex? sex, int? maintenance)
    {
        var floor = Floor(sex);
        if (maintenance is not { } m) return floor;
        var byDeficit = (int)(Math.Ceiling(m * (1 - MaxDeficitLimit) / 10) * 10);
        return Math.Max(floor, byDeficit);
    }

    public static CalorieSuggestion Suggest(GoalType goal, int maintenance, Sex sex)
    {
        switch (goal)
        {
            case GoalType.Lose:
            {
                var deficit = Math.Clamp(LoseDeficit, maintenance * MinDeficitShare, maintenance * MaxDeficitShare);
                var raw = BodyMetrics.RoundTo10(maintenance - deficit);
                var min = SafeMinimum(sex, maintenance);
                if (min >= maintenance)
                    return new(maintenance, maintenance,
                        $"Your maintenance is already at the safe minimum of {K(Floor(sex))} kcal, so there is no room for a deficit.");
                if (raw < min)
                    return new(min, maintenance, $"Raised to the safe minimum of {K(min)} kcal.");
                return new(raw, maintenance);
            }
            case GoalType.Gain:
            {
                var surplus = Math.Clamp(maintenance * SurplusShare, MinSurplus, MaxSurplus);
                return new(BodyMetrics.RoundTo10(maintenance + surplus), maintenance);
            }
            case GoalType.Maintain:
                return new(maintenance, maintenance);
            default:
                throw new ArgumentOutOfRangeException(nameof(goal));
        }
    }

    /// <summary>Error text for a goal the user typed, or null when it is allowed.</summary>
    public static string? CheckGoal(int kcal, Sex? sex, int? maintenance)
    {
        if (kcal > MaxGoal) return $"A daily goal can be at most {K(MaxGoal)} kcal.";

        var floor = Floor(sex);
        if (kcal < floor)
            return sex is null
                ? $"A daily goal must be at least {K(floor)} kcal."
                : $"A daily goal must be at least {K(floor)} kcal, the safe minimum for {(sex == Sex.Male ? "men" : "women")}.";

        var min = SafeMinimum(sex, maintenance);
        if (kcal < min)
            return $"That is more than 25% under your maintenance. The lowest safe goal is {K(min)} kcal.";
        return null;
    }

    public static string? CheckConsumed(int kcal) =>
        kcal is < 0 or > MaxConsumed ? $"Calories must be between 0 and {K(MaxConsumed)}." : null;

    /// <summary>Lose: at or under the goal. Gain: at or over. Maintain: within the tolerance either way.</summary>
    public static bool HitTarget(GoalType goal, int target, int consumed) => goal switch
    {
        GoalType.Lose => consumed <= target,
        GoalType.Gain => consumed >= target,
        GoalType.Maintain => Math.Abs(consumed - target) <= target * MaintainTolerance,
        _ => false
    };

    /// <summary>The window that counts as a hit for maintain, rounded inwards to whole kcal.</summary>
    public static (int Low, int High) MaintainRange(int target) =>
        ((int)Math.Ceiling(target * (1 - MaintainTolerance)), (int)Math.Floor(target * (1 + MaintainTolerance)));

    /// <summary>One line for the UI, e.g. "at or under 2,260 kcal".</summary>
    public static string TargetRule(GoalType goal, int target)
    {
        var (lo, hi) = MaintainRange(target);
        return goal switch
        {
            GoalType.Lose => $"at or under {K(target)} kcal",
            GoalType.Gain => $"at or over {K(target)} kcal",
            GoalType.Maintain => $"between {K(lo)} and {K(hi)} kcal",
            _ => ""
        };
    }
}
