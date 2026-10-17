using GymApp.Core;

namespace GymApp.Tests;

public class LeaderboardTests
{
    private const int Me = 1;

    private static LeaderboardEntry E(int id, string name, int points, int total = 0, int streak = 0) =>
        new(id, name, points, total, streak);

    private static IReadOnlyList<LeaderboardRow> Rank(params LeaderboardEntry[] entries) => Leaderboard.Rank(entries, Me);

    [Fact] public void OrderedByPoints_HighestFirst()
    {
        var rows = Rank(E(Me, "me", 20), E(2, "ana", 55), E(3, "bob", 5));

        Assert.Equal(["ana", "me", "bob"], rows.Select(r => r.Username));
        Assert.Equal([1, 2, 3], rows.Select(r => r.Rank));
        Assert.Equal([55, 20, 5], rows.Select(r => r.Points));
    }

    [Fact] public void Ties_ShareARank_AndTheNextRankSkips()
    {
        var rows = Rank(E(Me, "me", 30), E(2, "ana", 30), E(3, "bob", 10), E(4, "cat", 10), E(5, "dan", 0));
        Assert.Equal([1, 1, 3, 3, 5], rows.Select(r => r.Rank));
    }

    [Fact] public void StreakDoesNotBreakTies()
    {
        var rows = Rank(E(Me, "me", 30, streak: 1), E(2, "ana", 30, streak: 40));
        Assert.Equal([1, 1], rows.Select(r => r.Rank));
    }

    [Fact] public void WithinATie_ByUsernameIgnoringCase()
    {
        var rows = Rank(E(Me, "Mike", 10), E(2, "bob", 10), E(3, "Alice", 10), E(4, "carl", 10));
        Assert.Equal(["Alice", "bob", "carl", "Mike"], rows.Select(r => r.Username));
    }

    [Fact] public void ViewerAlone_WithZeroPoints()
    {
        var row = Assert.Single(Rank(E(Me, "me", 0)));
        Assert.Equal((1, 0, true), (row.Rank, row.Points, row.IsYou));
        Assert.Equal("Beginner", row.Level.Name);
    }

    [Fact] public void OnlyTheViewerIsYou()
    {
        var rows = Rank(E(Me, "me", 5), E(2, "ana", 10));
        Assert.Equal([false, true], rows.Select(r => r.IsYou));
    }

    [Fact] public void ViewerMissing_Throws() =>
        Assert.Throws<ArgumentException>(() => Leaderboard.Rank([E(2, "ana", 10)], Me));

    [Fact] public void DuplicateUser_CountsOnce()
    {
        var rows = Rank(E(Me, "me", 5), E(2, "ana", 10), E(2, "ana", 10));
        Assert.Equal(2, rows.Count);
    }

    [Fact] public void Level_ComesFromTotalPoints_NotPeriodPoints()
    {
        // 40 this week, 1600 all time: Athlete, not Beginner
        var row = Assert.Single(Rank(E(Me, "me", 40, total: 1600)));
        Assert.Equal(40, row.Points);
        Assert.Equal((5, "Athlete"), (row.Level.Number, row.Level.Name));
    }

    [Fact] public void StreakIsCarriedOnEachRow()
    {
        var rows = Rank(E(Me, "me", 5, streak: 3), E(2, "ana", 10, streak: 12));
        Assert.Equal([12, 3], rows.Select(r => r.CurrentStreak));
    }

    // 2026-10-05 is a Monday
    [Theory]
    [InlineData(5)]    // Monday
    [InlineData(8)]    // Thursday
    [InlineData(11)]   // Sunday
    public void WeekRange_MondayToSunday(int day)
    {
        var (monday, sunday) = Leaderboard.WeekRange(new DateOnly(2026, 10, day));
        Assert.Equal(new DateOnly(2026, 10, 5), monday);
        Assert.Equal(new DateOnly(2026, 10, 11), sunday);
    }

    [Fact] public void WeekRange_NextMondayStartsANewWeek() =>
        Assert.Equal(new DateOnly(2026, 10, 12), Leaderboard.WeekRange(new DateOnly(2026, 10, 12)).Monday);

    [Fact] public void WeekTitle()
    {
        Assert.Equal("Week of 12–18 Oct", Leaderboard.WeekTitle(new DateOnly(2026, 10, 15)));
        Assert.Equal("Week of 28 Sep – 4 Oct", Leaderboard.WeekTitle(new DateOnly(2026, 10, 1)));
    }
}
