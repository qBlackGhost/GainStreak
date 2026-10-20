using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using GymApp.Services;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

public class StatsProjectionServiceTests : IDisposable
{
    // 2026-10-05 is a Monday. D(0) = Mon ... D(7) = next Mon
    private static readonly DateOnly Mon = new(2026, 10, 5);
    private static DateOnly D(int n) => Mon.AddDays(n);

    private readonly TestDb _db = new();
    private readonly StatsProjectionService _svc;
    private readonly PointsService _points;
    private readonly StreakService _streak;
    private readonly int _alice;

    public StatsProjectionServiceTests()
    {
        _svc = new StatsProjectionService(_db);
        _points = new PointsService(_db);
        _streak = new StreakService(_db);
        _alice = _db.AddUser("alice");
        Seed(db => db.UserSchedules.Add(new UserSchedule { UserId = _alice, EffectiveFrom = Mon, WeekPattern = "WWRWWRR" }));
    }

    public void Dispose() => _db.Dispose();

    private void Seed(Action<AppDbContext> change)
    {
        using var db = _db.CreateDbContext();
        change(db);
        db.SaveChanges();
    }

    private async Task SyncAsync(DateOnly day)
    {
        await _streak.RecalculateAsync(_alice, day);
        await _points.SyncAsync(_alice, day);
    }

    [Fact] public async Task UnknownUser_IsNull() =>
        Assert.Null(await _svc.ProjectAsync(9999, D(0)));

    [Fact] public async Task MatchesOpeningTheAppEachDay()
    {
        // Mon, Wed (pays Tue), Thu, Fri, a calorie target on Thu, a manual adjustment; then nothing more
        Seed(db =>
        {
            db.WorkoutLogs.AddRange(new[] { 0, 2, 3, 4 }.Select(d => new WorkoutLog { UserId = _alice, Date = D(d) }));
            db.CalorieLogs.Add(new CalorieLog
                { UserId = _alice, Date = D(3), CaloriesConsumed = 1800, GoalAtTime = 1800, GoalTypeAtTime = GoalType.Lose });
            db.PointsEntries.Add(new PointsEntry
                { UserId = _alice, Reason = PointsReason.Adjustment, Amount = 7, Date = D(1), DedupKey = "adjust:test" });
        });
        await _points.RebuildAsync(_alice, D(4));
        await _streak.RecalculateAsync(_alice, D(4));

        var projected = await _svc.ProjectAsync(_alice, D(4), 20);
        Assert.NotNull(projected);

        // What friends would really see if alice opened the app every day without logging anything
        for (var k = 0; k < 20; k++)
        {
            var day = D(4 + k);
            await SyncAsync(day);

            await using var db = _db.CreateDbContext();
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == _alice);
            var monday = PointsCalculator.MondayOf(day);
            var week = await db.PointsEntries.Where(p => p.UserId == _alice && p.Date >= monday && p.Date <= monday.AddDays(6))
                .SumAsync(p => p.Amount);

            Assert.Equal(new ProjectedStats(day, user.Points, week, user.CurrentStreak), projected[k]);
        }
    }

    [Fact] public async Task OnlyReads()
    {
        Seed(db => db.WorkoutLogs.Add(new WorkoutLog { UserId = _alice, Date = D(0) }));
        await SyncAsync(D(0));

        await _svc.ProjectAsync(_alice, D(0));

        await using var db = _db.CreateDbContext();
        Assert.Equal(1, await db.PointsEntries.CountAsync(p => p.UserId == _alice));
        Assert.Equal(1, (await db.Users.SingleAsync(u => u.Id == _alice)).CurrentStreak);
    }
}
