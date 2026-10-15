namespace GymApp.Core;

public record Level(int Number, string Name, int From);

/// <summary>Where a points total sits. <see cref="NextFrom"/> and <see cref="NextName"/> are null at the top level;
/// <see cref="Progress"/> runs from 0 to 1 within the current level (1 at the top).</summary>
public record LevelInfo(int Number, string Name, int From, int? NextFrom, string? NextName, double Progress)
{
    public int? PointsToNext(int points) => NextFrom - points;
}

/// <summary>Levels come from the points total only, so they go down when points are removed.</summary>
public static class LevelCalculator
{
    /// <summary>The level table, lowest first. Tune it here.</summary>
    public static readonly IReadOnlyList<Level> Levels =
    [
        new(1, "Beginner", 0),
        new(2, "Novice", 100),
        new(3, "Regular", 300),
        new(4, "Dedicated", 700),
        new(5, "Athlete", 1500),
        new(6, "Elite", 3000),
        new(7, "Legend", 6000),
    ];

    public static LevelInfo For(int points)
    {
        var i = Levels.Count - 1;
        while (i > 0 && points < Levels[i].From) i--;

        var level = Levels[i];
        if (i == Levels.Count - 1)
            return new LevelInfo(level.Number, level.Name, level.From, null, null, 1);

        var next = Levels[i + 1];
        var progress = Math.Clamp((double)(points - level.From) / (next.From - level.From), 0, 1);
        return new LevelInfo(level.Number, level.Name, level.From, next.From, next.Name, progress);
    }
}
