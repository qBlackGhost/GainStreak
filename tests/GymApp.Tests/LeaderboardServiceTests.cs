using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using GymApp.Services;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

public class LeaderboardServiceTests : IDisposable
{
    // 2026-10-05 is a Monday; the board is read on Sunday 2026-10-11, so this week is 5–11 Oct.
    // No schedules, so only workouts earn points here (+5 each).
    private static readonly DateOnly Today = new(2026, 10, 11);
    private static DateOnly Oct(int day) => new(2026, 10, day);

    private readonly TestDb _db = new();
    private readonly FriendService _friends;
    private readonly LeaderboardService _svc;
    private readonly int _alice, _bob, _carol, _dan;

    public LeaderboardServiceTests()
    {
        var store = new LocalSocialStore(_db);
        _friends = new FriendService(store);
        _svc = new LeaderboardService(store);
        _alice = _db.AddUser("alice");
        _bob = _db.AddUser("bob");
        _carol = _db.AddUser("carol");
        _dan = _db.AddUser("dan");
    }

    public void Dispose() => _db.Dispose();

    private void Seed(Action<AppDbContext> change)
    {
        using var db = _db.CreateDbContext();
        change(db);
        db.SaveChanges();
    }

    private void Workouts(int userId, params int[] octDays) =>
        Seed(db => db.WorkoutLogs.AddRange(octDays.Select(d => new WorkoutLog { UserId = userId, Date = Oct(d) })));

    private async Task MakeFriends(int x, int y)
    {
        await _friends.SendRequestAsync(x, y);
        await _friends.AcceptAsync(y, x);
    }

    private async Task<LeaderboardView> Board(int userId, LeaderboardPeriod period = LeaderboardPeriod.ThisWeek) =>
        (await _svc.GetAsync(userId, period, Today))!;

    [Fact] public async Task OnlyYouAndAcceptedFriends()
    {
        await MakeFriends(_alice, _bob);
        await _friends.SendRequestAsync(_alice, _carol);   // pending, not on the board
        await _friends.SendRequestAsync(_dan, _alice);     // pending the other way
        // dan and carol are also strangers to bob

        Assert.Equal(["alice", "bob"], (await Board(_alice)).Rows.Select(r => r.Username).Order());
        Assert.Equal(["alice", "bob"], (await Board(_bob)).Rows.Select(r => r.Username).Order());
        Assert.Equal(["carol"], (await Board(_carol)).Rows.Select(r => r.Username));
    }

    [Fact] public async Task FriendsOfFriends_AreNotShown()
    {
        await MakeFriends(_alice, _bob);
        await MakeFriends(_bob, _carol);
        Assert.Equal(["alice", "bob"], (await Board(_alice)).Rows.Select(r => r.Username).Order());
    }

    [Fact] public async Task Alone_ShowsJustYou()
    {
        var board = await Board(_alice);
        Assert.True(board.IsAlone);
        var row = Assert.Single(board.Rows);
        Assert.Equal((1, "alice", 0, true), (row.Rank, row.Username, row.Points, row.IsYou));
        Assert.Same(row, board.You);
    }

    [Fact] public async Task UnknownUser_IsNull() =>
        Assert.Null(await _svc.GetAsync(9999, LeaderboardPeriod.ThisWeek, Today));

    [Fact] public async Task ThisWeek_CountsOnlyThisWeeksPoints()
    {
        await MakeFriends(_alice, _bob);
        Workouts(_bob, 4, 5, 7);       // last Sunday, then Mon and Wed this week
        Workouts(_alice, 9);

        var board = await Board(_alice);
        Assert.Equal((LeaderboardPeriod.ThisWeek, Oct(5), Oct(11)), (board.Period, board.WeekStart, board.WeekEnd));
        Assert.Equal([("bob", 10, 1), ("alice", 5, 2)], board.Rows.Select(r => (r.Username, r.Points, r.Rank)));
        Assert.False(board.IsAlone);
    }

    [Fact] public async Task WeekPoints_LeaveOutLastSundayAndNextMonday()
    {
        // Straight from the store, since a sync would drop a ledger entry dated after today
        Seed(db => db.PointsEntries.AddRange(
            new PointsEntry { UserId = _bob, Reason = PointsReason.Workout, Amount = 5, Date = Oct(4), DedupKey = "a" },
            new PointsEntry { UserId = _bob, Reason = PointsReason.Workout, Amount = 7, Date = Oct(5), DedupKey = "b" },
            new PointsEntry { UserId = _bob, Reason = PointsReason.WeekCompleteGrace, Amount = 25, Date = Oct(11), DedupKey = "c" },
            new PointsEntry { UserId = _bob, Reason = PointsReason.Workout, Amount = 100, Date = Oct(12), DedupKey = "d" }));

        await using var s = await new LocalSocialStore(_db).OpenAsync();
        var stats = await s.GetPublicStatsAsync([_bob, _alice], Oct(5), Oct(11));
        Assert.Equal(32, stats.Single(x => x.UserId == _bob).WeekPoints);
        Assert.Equal(0, stats.Single(x => x.UserId == _alice).WeekPoints);
    }

    [Fact] public async Task AllTime_UsesTotalPoints()
    {
        await MakeFriends(_alice, _bob);
        Workouts(_bob, 4, 5);              // 10 total, 5 this week
        Workouts(_alice, 1, 2, 3, 4);      // 20 total, nothing this week

        var week = await Board(_alice);
        Assert.Equal([("bob", 5), ("alice", 0)], week.Rows.Select(r => (r.Username, r.Points)));

        var all = await Board(_alice, LeaderboardPeriod.AllTime);
        Assert.Equal(LeaderboardPeriod.AllTime, all.Period);
        Assert.Equal([("alice", 20), ("bob", 10)], all.Rows.Select(r => (r.Username, r.Points)));
    }

    [Fact] public async Task Level_IsTheAllTimeLevel_OnTheWeeklyBoard()
    {
        Seed(db =>
        {
            db.Users.Single(u => u.Id == _alice).PointsBackfilled = true;
            // Frozen: dated before the sync window, so the refresh keeps it
            db.PointsEntries.Add(new PointsEntry { UserId = _alice, Reason = PointsReason.WeekComplete, Amount = 400, Date = Oct(1).AddMonths(-1), DedupKey = "old" });
        });

        var row = Assert.Single((await Board(_alice)).Rows);
        Assert.Equal(0, row.Points);
        Assert.Equal("Regular", row.Level.Name);   // 400 all time
    }

    [Fact] public async Task StreakIsShownForEachPerson()
    {
        await MakeFriends(_alice, _bob);
        Workouts(_alice, 9, 10, 11);
        Workouts(_bob, 11);

        var rows = (await Board(_alice)).Rows;
        Assert.Equal(3, rows.Single(r => r.Username == "alice").CurrentStreak);
        Assert.Equal(1, rows.Single(r => r.Username == "bob").CurrentStreak);
    }

    [Fact] public async Task FriendsBrokenStreak_ShowsZero_EvenWithAStaleCache()
    {
        await MakeFriends(_alice, _bob);
        Workouts(_bob, 4, 5, 6, 7, 8);   // missed the 9th, broke on the 10th
        Seed(db => db.Users.Single(u => u.Id == _bob).CurrentStreak = 5);   // cached on the 8th, never opened since

        var bob = (await Board(_alice)).Rows.Single(r => r.Username == "bob");
        Assert.Equal(0, bob.CurrentStreak);

        using var db = _db.CreateDbContext();
        Assert.Equal(0, db.Users.Single(u => u.Id == _bob).CurrentStreak);   // the cache was refreshed too
    }

    [Fact] public async Task InactiveFriend_ShowsZero_OnADayThatUsedToGiveOne()
    {
        await MakeFriends(_alice, _bob);
        Workouts(_bob, 4, 5, 6, 7);   // missed the 8th, broke on the 9th; before 12a the 10th's miss counted +1 again
        var bob = (await Board(_alice)).Rows.Single(r => r.Username == "bob");
        Assert.Equal(0, bob.CurrentStreak);
    }

    [Fact] public async Task FriendsStalePoints_AreSynced()
    {
        await MakeFriends(_alice, _bob);
        Workouts(_bob, 5, 6);   // logged, but bob's sync never ran

        var bob = (await Board(_alice, LeaderboardPeriod.AllTime)).Rows.Single(r => r.Username == "bob");
        Assert.Equal(10, bob.Points);
    }

    [Fact] public async Task RemovingAFriend_TakesThemOffTheBoard()
    {
        await MakeFriends(_alice, _bob);
        await MakeFriends(_alice, _carol);
        Assert.Equal(3, (await Board(_alice)).Rows.Count);

        await _friends.RemoveAsync(_bob, _alice);
        Assert.Equal(["alice", "carol"], (await Board(_alice)).Rows.Select(r => r.Username).Order());
        Assert.True((await Board(_bob)).IsAlone);
    }
}
