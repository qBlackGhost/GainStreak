using GymApp.App.ViewModels;
using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;

namespace GymApp.Tests;

/// <summary>Profile → Vacation and Home's I'm back (0.19.0), on dates around the real today.</summary>
public sealed class VacationCardViewModelTests : IDisposable
{
    private static readonly DateOnly Today = AppClock.Today;
    private readonly TestDb _db = new();
    private readonly VacationService _svc;
    private readonly int _alice;
    private int _changed;

    public VacationCardViewModelTests()
    {
        var streaks = new StreakService(_db);
        _svc = new VacationService(_db, streaks, new PointsService(_db));
        _alice = _db.AddUser("alice");
        using var db = _db.CreateDbContext();
        db.UserSchedules.Add(new UserSchedule { UserId = _alice, EffectiveFrom = Today.AddDays(-30), WeekPattern = "WWWWWWW" });
        db.WorkoutLogs.AddRange(new WorkoutLog { UserId = _alice, Date = Today.AddDays(-2) }, new WorkoutLog { UserId = _alice, Date = Today.AddDays(-1) });
        db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private VacationCardViewModel Card(int? user = null) =>
        new(_svc, () => user ?? _alice, () => { _changed++; return Task.CompletedTask; });

    private static string Short(DateOnly d) => VacationSheetViewModel.Short(d);

    [Fact]
    public async Task Available_PlanAndStart()
    {
        var card = Card();
        await card.LoadAsync();
        Assert.Equal("Available", card.StateText);
        Assert.True(card.CanPlan);

        await card.PlanCommand.ExecuteAsync(null);
        var sheet = card.Sheet!;
        Assert.Equal(7, sheet.Days.Count);
        Assert.Equal(Today.AddDays(3), sheet.ReturnDate);
        Assert.Equal($"Frozen {Short(Today)} to {Short(Today.AddDays(2))}", sheet.FrozenRange);
        Assert.Null(sheet.StartsTomorrowNote);

        sheet.Days[0].PickCommand.Execute(null);
        Assert.Equal("Frozen " + Short(Today), sheet.FrozenRange);
        Assert.StartsWith("1 day · back on", sheet.FrozenDetail);

        await sheet.StartCommand.ExecuteAsync(null);
        Assert.Null(card.Sheet);
        Assert.Equal(1, _changed);
        Assert.True(card.HasVacation);
        Assert.Equal("On vacation · back on " + Short(Today.AddDays(1)), card.StateText);
        Assert.Equal("I'm back", card.EndLabel);
        Assert.False(card.CanEnd);                                     // a one-day vacation: back tomorrow anyway
    }

    [Fact]
    public async Task AfterTheVacation_TheCooldownShows()
    {
        using (var db = _db.CreateDbContext())
        {
            db.Vacations.Add(new Vacation { UserId = _alice, StartDate = Today.AddDays(-10), ReturnDate = Today.AddDays(-5) });
            db.SaveChanges();
        }
        var card = Card();
        await card.LoadAsync();
        Assert.False(card.HasVacation);
        Assert.False(card.CanPlan);
        Assert.Equal("Next available on " + Short(Today.AddDays(50)), card.StateText);
    }

    [Fact]
    public async Task ImBack_EndsIt_AndShowsTheCooldown()
    {
        await _svc.StartAsync(_alice, Today.AddDays(4));
        var card = Card();
        await card.LoadAsync();

        card.AskEndCommand.Execute(null);
        Assert.True(card.IsConfirmingEnd);
        Assert.Equal("End your vacation?", card.EndTitle);
        await card.EndCommand.ExecuteAsync(null);

        Assert.False(card.IsConfirmingEnd);
        Assert.False(card.CanEnd);                                     // today stays frozen; nothing left to end
        Assert.Equal("On vacation · back on " + Short(Today.AddDays(1)), card.StateText);
        Assert.Equal("Vacation ended. Your streak counts again from tomorrow.", card.LastChange);
        Assert.Equal(1, _changed);
    }

    [Fact]
    public async Task AfterTodaysWorkout_ItStartsTomorrow_AndCanBeCancelled()
    {
        using (var db = _db.CreateDbContext())
        {
            db.WorkoutLogs.Add(new WorkoutLog { UserId = _alice, Date = Today });
            db.SaveChanges();
        }
        var card = Card();
        await card.LoadAsync();
        await card.PlanCommand.ExecuteAsync(null);
        Assert.NotNull(card.Sheet!.StartsTomorrowNote);
        await card.Sheet.StartCommand.ExecuteAsync(null);

        Assert.Equal("Starts tomorrow · back on " + Short(Today.AddDays(4)), card.StateText);
        Assert.Equal("Cancel vacation", card.EndLabel);
        card.AskEndCommand.Execute(null);
        await card.EndCommand.ExecuteAsync(null);
        Assert.Equal("Vacation cancelled.", card.LastChange);
        Assert.Equal("Available", card.StateText);
    }

    [Fact]
    public async Task AMissedDayToChooseFor_SaysSoAndCantPlan()
    {
        var bob = _db.AddUser("bob");
        using (var db = _db.CreateDbContext())
        {
            db.UserSchedules.Add(new UserSchedule { UserId = bob, EffectiveFrom = Today.AddDays(-30), WeekPattern = "WWWWWWW" });
            db.WorkoutLogs.Add(new WorkoutLog { UserId = bob, Date = Today.AddDays(-2) });   // yesterday missed
            db.SaveChanges();
        }
        var card = Card(bob);
        await card.LoadAsync();
        Assert.False(card.CanPlan);
        Assert.Equal(VacationService.RefusalText(new VacationCheck(VacationRefusal.SettleMissedDay)), card.RefusalText);
    }

    [Fact]
    public void TheSheet_HasAView_TheViewLocatorFinds()
    {
        var sheet = new VacationSheetViewModel(_svc, _alice, _ => Task.CompletedTask, () => { });
        Assert.True(new GymApp.App.ViewLocator().Match(sheet));
        Assert.NotNull(typeof(GymApp.App.ViewLocator).Assembly.GetType("GymApp.App.Views.VacationSheetView"));
    }

    [Fact]
    public void WeekChip_VacationToday_HasItsOwnLook()
    {
        var chip = new WeekChipViewModel(new WeekChip(Today, WeekChipKind.Vacation, IsToday: true), "Push");
        Assert.True(chip.IsVacation);
        Assert.True(chip.IsToday);
        Assert.False(chip.IsTodayOpen);
        Assert.Equal("Vacation", chip.Tip);
    }
}
