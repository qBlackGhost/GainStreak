using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Desktop.ViewModels;
using GymApp.Services;

namespace GymApp.Tests;

public class PointsHistoryViewModelTests : IDisposable
{
    // Weeks long before today, so they get date labels rather than "This week" / "Last week".
    // 2025-09-08 and 2025-09-29 are Mondays.
    private readonly TestDb _db = new();
    private readonly PointsHistoryViewModel _vm;

    public PointsHistoryViewModelTests()
    {
        var alice = _db.AddUser("alice");
        using (var db = _db.CreateDbContext())
        {
            void Add(PointsReason reason, int amount, DateOnly date) => db.PointsEntries.Add(new PointsEntry
                { UserId = alice, Reason = reason, Amount = amount, Date = date, DedupKey = $"{reason}:{date:yyyyMMdd}" });
            Add(PointsReason.Workout, 5, new(2025, 9, 8));
            Add(PointsReason.CalorieTarget, 10, new(2025, 9, 10));
            Add(PointsReason.WeekComplete, 50, new(2025, 9, 14));
            Add(PointsReason.Workout, 5, new(2025, 10, 1));
            Add(PointsReason.Adjustment, -3, new(2025, 10, 2));
            db.Users.Single(u => u.Id == alice).Points = 67;   // the cached total the popup shows
            db.SaveChanges();
        }
        var session = new SessionService();
        using (var db = _db.CreateDbContext()) session.SignIn(db.Users.Single(u => u.Id == alice));
        _vm = new PointsHistoryViewModel(new PointsService(_db), session);
    }

    public void Dispose() => _db.Dispose();

    [Fact] public async Task Open_GroupsByWeekNewestFirst_WithSubtotals()
    {
        await _vm.Open(() => { });

        Assert.Equal("67", _vm.TotalText);
        Assert.Equal([("29 Sep – 5 Oct", "+2"), ("8 – 14 Sep", "+65")], _vm.Weeks.Select(w => (w.Label, w.SumText)));
        Assert.Equal(["Week complete", "Calorie target", "Workout"], _vm.Weeks[1].Rows.Select(r => r.ReasonText));
        var adjustment = _vm.Weeks[0].Rows[0];
        Assert.Equal(("−3", true, true), (adjustment.AmountText, adjustment.IsNegative, adjustment.IsAdjustment));
        Assert.False(_vm.IsEmpty);
    }

    [Fact] public async Task Filter_KeepsOnlyThatKind_AndSaysHowToEarnWhenEmpty()
    {
        await _vm.Open(() => { });

        _vm.ShowWeeksCommand.Execute(null);
        Assert.Equal(2, _vm.FilterIndex);
        var week = Assert.Single(_vm.Weeks);
        Assert.Equal(("+50", true), (week.Rows.Single().AmountText, week.Rows.Single().IsWeek));

        _vm.ShowCaloriesCommand.Execute(null);
        Assert.Equal(["Calorie target"], _vm.Weeks.SelectMany(w => w.Rows).Select(r => r.ReasonText));

        _vm.ShowWorkoutsCommand.Execute(null);
        Assert.Equal(2, _vm.Weeks.SelectMany(w => w.Rows).Count());
    }

    [Fact] public async Task Open_StartsFromAll_AndCloseCallsBack()
    {
        var closed = false;
        await _vm.Open(() => closed = true);
        _vm.ShowWeeksCommand.Execute(null);

        await _vm.Open(() => closed = true);
        Assert.True(_vm.IsAll);

        _vm.CloseCommand.Execute(null);
        Assert.True(closed);
    }

    [Fact] public async Task Open_ShowsPointsToTheNextLevel()
    {
        await _vm.Open(() => { });

        Assert.Equal(("Level 1 · Beginner", "33", " to Novice"), (_vm.LevelText, _vm.ToNextAmount, _vm.ToNextText));
        Assert.Equal(0.67, _vm.LevelProgress, 2);
    }
}
