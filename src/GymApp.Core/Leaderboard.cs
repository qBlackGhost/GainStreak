using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GymApp.Core;

/// <summary>A friendship row's state. Stored as its name in the database, so don't rename the members.
/// Declined, cancelled and removed friendships are deleted, not stored.</summary>
public enum FriendshipStatus { Pending, Accepted }

/// <summary>Which points the board ranks by: this week's (Monday to Sunday) or the all-time total.</summary>
public enum LeaderboardPeriod { ThisWeek, AllTime }

/// <summary>One person going into the board. <see cref="PeriodPoints"/> is what they're ranked by;
/// <see cref="TotalPoints"/> only sets the level.</summary>
public record LeaderboardEntry(int UserId, string Username, int PeriodPoints, int TotalPoints, int CurrentStreak, string? Avatar = null);

/// <summary>One row of the board. Rows tied on points share a rank. The level is always the all-time one.</summary>
public record LeaderboardRow(int Rank, int UserId, string Username, int Points, LevelInfo Level, int CurrentStreak, bool IsYou, string? Avatar = null);

/// <summary>The friends leaderboard. Only public stats go in: username, points and streak.</summary>
public static class Leaderboard
{
    private static readonly StringComparer NameOrder = StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.IgnoreCase);

    /// <summary>Monday to Sunday of the week containing <paramref name="d"/>.</summary>
    public static (DateOnly Monday, DateOnly Sunday) WeekRange(DateOnly d)
    {
        var monday = PointsCalculator.MondayOf(d);
        return (monday, monday.AddDays(6));
    }

    /// <summary>"Week of 12–18 Oct", or "Week of 28 Sep – 4 Oct" when it spans two months.</summary>
    public static string WeekTitle(DateOnly d)
    {
        var (monday, sunday) = WeekRange(d);
        var ci = CultureInfo.InvariantCulture;
        return monday.Month == sunday.Month
            ? $"Week of {monday.Day}–{sunday.ToString("d MMM", ci)}"
            : $"Week of {monday.ToString("d MMM", ci)} – {sunday.ToString("d MMM", ci)}";
    }

    /// <summary>
    /// The rows in rank order. Rules:
    ///  - Most period points first. Ties share a rank and the next rank skips ("1, 1, 3"); streak doesn't break ties.
    ///  - Within a tie, by username ignoring case, so the order doesn't jump between refreshes.
    ///  - The viewer must be in <paramref name="entries"/>; they're on the board even alone with 0 points.
    ///  - A user listed twice counts once (the first entry).
    /// </summary>
    public static IReadOnlyList<LeaderboardRow> Rank(IEnumerable<LeaderboardEntry> entries, int viewerId)
    {
        var ordered = entries
            .DistinctBy(e => e.UserId)
            .OrderByDescending(e => e.PeriodPoints)
            .ThenBy(e => e.Username, NameOrder)
            .ThenBy(e => e.UserId)
            .ToList();

        if (!ordered.Any(e => e.UserId == viewerId))
            throw new ArgumentException("The viewing user must be on the board.", nameof(entries));

        var rows = new List<LeaderboardRow>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var e = ordered[i];
            var rank = i > 0 && e.PeriodPoints == ordered[i - 1].PeriodPoints ? rows[i - 1].Rank : i + 1;
            rows.Add(new LeaderboardRow(rank, e.UserId, e.Username, e.PeriodPoints, LevelCalculator.For(e.TotalPoints),
                e.CurrentStreak, e.UserId == viewerId, e.Avatar));
        }
        return rows;
    }
}
