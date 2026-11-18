using System.Text.Json;
using GymApp.Core;
using GymApp.Data.Models;
using GymApp.App;
using GymApp.App.ViewModels;
using GymApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GymApp.Tests;

/// <summary>Phase 24: the widgets' links.</summary>
public class WidgetLinkTests
{
    [Theory]
    [InlineData("gainstreak://open/home", WidgetTarget.Home)]
    [InlineData("gainstreak://open/plan", WidgetTarget.Plan)]
    [InlineData("gainstreak://open/points", WidgetTarget.Points)]
    [InlineData("gainstreak://open/body", WidgetTarget.Body)]
    [InlineData("gainstreak://open/start", WidgetTarget.StartWorkout)]
    [InlineData("GainStreak://Open/Plan/", WidgetTarget.Plan)]
    public void TryParse_KnownTargets(string link, WidgetTarget target) =>
        Assert.Equal(target, WidgetLink.TryParse(new Uri(link)));

    [Theory]
    [InlineData("gainstreak://open/nowhere")]
    [InlineData("gainstreak://other/home")]
    [InlineData("https://gainstreak.app/add/ABCD1234")]
    [InlineData("https://open/home")]
    public void TryParse_OtherLinks_AreNotWidgetLinks(string link) => Assert.Null(WidgetLink.TryParse(new Uri(link)));

    [Fact] public void For_RoundTrips()
    {
        foreach (var target in Enum.GetValues<WidgetTarget>())
            Assert.Equal(target, WidgetLink.TryParse(WidgetLink.For(target)));
    }
}

/// <summary>Phase 24: what the widgets show, built with Home's rules.</summary>
public class WidgetSnapshotServiceTests : IDisposable
{
    // 2026-10-05 is a Monday: Legs (4 exercises); Tuesday is a rest day
    private static readonly DateOnly Mon = new(2026, 10, 5);

    private readonly TestDb _db = new();
    private readonly WorkoutService _workouts;
    private readonly WidgetSnapshotService _svc;
    private readonly int _alice;
    private readonly int _legs;
    private readonly List<int> _ex;

    public WidgetSnapshotServiceTests()
    {
        _alice = _db.AddUser("alice");
        var days = _db.AddSplit(_alice, ("Legs", DayOfWeek.Monday), ("Arms", DayOfWeek.Wednesday));
        _legs = days[0];
        _ex = _db.AddDayExercises(_alice, _legs, 6, workingSets: 2);
        using (var db = _db.CreateDbContext())
        {
            db.UserSchedules.Add(new UserSchedule { UserId = _alice, EffectiveFrom = Mon, WeekPattern = "WRWRRRR" });
            db.SaveChanges();
        }
        var streaks = new StreakService(_db);
        _workouts = new WorkoutService(_db, streaks, new PointsService(_db));
        var live = new LiveWorkoutService(_db, _workouts, new SetService(_db, _workouts), streaks);
        _svc = new WidgetSnapshotService(_db, _workouts, streaks, live);
    }

    public void Dispose() => _db.Dispose();

    private void AddWeight(DateOnly date, double kg)
    {
        using var db = _db.CreateDbContext();
        db.WeightLogs.Add(new WeightLog { UserId = _alice, Date = date, WeightKg = kg });
        db.SaveChanges();
    }

    [Fact] public async Task ATrainingDay_NotLoggedYet_IsPending_WithTodaysExercises()
    {
        var s = await _svc.BuildAsync(_alice, Mon);

        Assert.True(s.SignedIn);
        Assert.Equal("2026-10-05", s.Date);
        Assert.Equal("pending", s.Streak!.Status);
        Assert.Equal("Legs", s.Today!.Name);
        Assert.Equal(6, s.Today.ExerciseCount);
        Assert.Equal(WidgetSnapshotService.MaxExercises, s.Today.Exercises.Count);
        Assert.All(s.Today.Exercises, e => Assert.Equal("First time", e.Last));
        // Tuesday rests
        Assert.Equal("2026-10-06", s.Tomorrow!.Date);
        Assert.True(s.Tomorrow.HasSplit);
        Assert.Null(s.Tomorrow.Name);
        Assert.Equal(new WidgetLevel(1, "Beginner", 0, 0, "Novice", 100), s.Level);
    }

    [Fact] public async Task ALoggedDay_IsDone_AndCounts()
    {
        using (var db = _db.CreateDbContext())
        {
            db.WorkoutLogs.Add(new WorkoutLog { UserId = _alice, Date = Mon, SplitDayId = _legs });
            db.Users.Single(u => u.Id == _alice).Points = 120;
            db.SaveChanges();
        }

        var s = await _svc.BuildAsync(_alice, Mon);

        Assert.Equal("done", s.Streak!.Status);
        Assert.Equal(1, s.Streak.Current);
        Assert.Equal(new WidgetLevel(2, "Novice", 120, 0.1, "Regular", 180), s.Level);
    }

    [Fact] public async Task ARestDay_IsRest()
    {
        var s = await _svc.BuildAsync(_alice, Mon.AddDays(1));

        Assert.Equal("rest", s.Streak!.Status);
        Assert.Null(s.Today!.Name);
        Assert.Equal("Arms", s.Tomorrow!.Name);
    }

    [Fact] public async Task NoSplit_IsNotStarted()
    {
        var bob = _db.AddUser("bob");

        var s = await _svc.BuildAsync(bob, Mon);

        Assert.Equal("notStarted", s.Streak!.Status);
        Assert.False(s.Today!.HasSplit);
    }

    [Fact] public async Task OnVacation_CarriesTheReturnDate()
    {
        using (var db = _db.CreateDbContext())
        {
            // Legs on Monday, then away from Wednesday (a training day) until Saturday
            db.WorkoutLogs.Add(new WorkoutLog { UserId = _alice, Date = Mon, SplitDayId = _legs });
            db.Vacations.Add(new Vacation { UserId = _alice, StartDate = Mon.AddDays(2), ReturnDate = Mon.AddDays(5) });
            db.SaveChanges();
        }

        var s = await _svc.BuildAsync(_alice, Mon.AddDays(2));

        Assert.Equal("vacation", s.Streak!.Status);
        Assert.Equal("2026-10-10", s.Streak.VacationUntil);
        Assert.Null((await _svc.BuildAsync(_alice, Mon.AddDays(5))).Streak!.VacationUntil);
    }

    [Fact] public async Task LastTime_IsTheHeaviestWorkingSet_AsHomeShowsIt()
    {
        using (var db = _db.CreateDbContext())
        {
            db.SetLogs.Add(new SetLog { UserId = _alice, ExerciseId = _ex[0], Date = Mon.AddDays(-7), SetNumber = 1, Kind = SetKind.Working, Reps = 8, WeightKg = 90 });
            db.SetLogs.Add(new SetLog { UserId = _alice, ExerciseId = _ex[0], Date = Mon.AddDays(-7), SetNumber = 2, Kind = SetKind.Working, Reps = 5, WeightKg = 100 });
            db.SaveChanges();
        }

        var s = await _svc.BuildAsync(_alice, Mon);

        Assert.Equal("100 kg × 5", s.Today!.Exercises[0].Last);
    }

    [Fact] public async Task Weight_IsTheLatestWeighIn_WithTheChangeOverAWeek()
    {
        AddWeight(Mon.AddDays(-40), 82);
        AddWeight(Mon.AddDays(-8), 80.6);
        AddWeight(Mon.AddDays(-1), 80);

        var w = (await _svc.BuildAsync(_alice, Mon)).Weight!;

        Assert.Equal("80.0", w.Current);
        Assert.Equal("kg", w.Unit);
        Assert.Equal("−0.6", w.Change7);
        Assert.Equal("−2", w.Change30);
        Assert.Equal([80.6, 80], w.Trend30);
    }

    [Fact] public async Task Weight_FollowsTheUnits_AndHasNoChangeWithoutAnOldEnoughWeighIn()
    {
        using (var db = _db.CreateDbContext())
        {
            db.UserProfiles.Add(new UserProfile { UserId = _alice, Units = UnitSystem.Imperial });
            db.SaveChanges();
        }
        AddWeight(Mon, 80);

        var w = (await _svc.BuildAsync(_alice, Mon)).Weight!;

        Assert.Equal("176.4", w.Current);
        Assert.Equal("lb", w.Unit);
        Assert.Null(w.Change7);
    }

    [Fact] public async Task NoWeighIns_NoWeight()
    {
        Assert.Null((await _svc.BuildAsync(_alice, Mon)).Weight);
    }

    [Fact] public async Task Steps_TodayIsTheLastDay_AndNoneWithoutHealth()
    {
        var s = await _svc.BuildAsync(_alice, Mon, [1000, 2000, 3000, 4000, 5000, 6000, 6240.4, 7000]);
        Assert.Equal(7000, s.Steps!.Today);
        Assert.Equal([2000, 3000, 4000, 5000, 6000, 6240, 7000], s.Steps.Week);

        Assert.Null((await _svc.BuildAsync(_alice, Mon)).Steps);
    }

    [Fact] public void Json_IsCamelCase_AndSignedOutCarriesNoData()
    {
        var json = WidgetSnapshot.SignedOut(Mon).ToJson();
        using var doc = JsonDocument.Parse(json);

        Assert.False(doc.RootElement.GetProperty("signedIn").GetBoolean());
        Assert.Equal("2026-10-05", doc.RootElement.GetProperty("date").GetString());
        Assert.Equal(WidgetSnapshot.CurrentVersion, doc.RootElement.GetProperty("version").GetInt32());
        Assert.False(doc.RootElement.TryGetProperty("streak", out _));
    }

    [Theory]
    [InlineData(-0.04, "0")]
    [InlineData(-0.6, "−0.6")]
    [InlineData(1.25, "+1.3")]
    [InlineData(2, "+2")]
    public void Change_HasASign(double change, string text) => Assert.Equal(text, WidgetFormat.Change(change));
}

/// <summary>Phase 24: a tapped widget or control opening its screen (<see cref="MainViewModel.OpenLink"/>), in the
/// TestBench's local mode.</summary>
[Collection(DataDirectoryCollection.Name)]
public sealed class OpenWidgetLinkTests : IAsyncLifetime
{
    private readonly TempDataDirectory _data = new();
    private ServiceProvider _sp = null!;

    public async Task InitializeAsync()
    {
        _sp = AppServices.Create(localSocial: true, new FakePlatform()).BuildServiceProvider();
        Assert.Null(await AppStartup.PrepareAsync(_sp, localSocial: true));
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        _data.Dispose();
    }

    private async Task<MainViewModel> SignedInAsync()
    {
        Assert.True((await _sp.GetRequiredService<IAccountService>().RegisterAsync("alice", "alice@example.com", "correct horse 1")).Success);
        var shell = _sp.GetRequiredService<MainViewModel>();
        shell.ShowHome();
        return shell;
    }

    private static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++) await Task.Delay(10);
        Assert.True(done());
    }

    [Fact] public async Task Body_OpensProfilesBodyData()
    {
        var shell = await SignedInAsync();

        Assert.True(shell.OpenLink(WidgetLink.For(WidgetTarget.Body)));

        Assert.Equal(ProfilePage.BodyData, Assert.IsType<ProfileViewModel>(shell.Current).Page);
    }

    [Fact] public async Task Points_OpensHomesPointsHistory()
    {
        var shell = await SignedInAsync();
        shell.ShowSplits();

        shell.OpenLink(WidgetLink.For(WidgetTarget.Points));

        Assert.True(Assert.IsType<HomeViewModel>(shell.Current).IsPointsHistoryOpen);
    }

    [Fact] public async Task Plan_WithoutASplit_OpensSplits()
    {
        var shell = await SignedInAsync();

        shell.OpenLink(WidgetLink.For(WidgetTarget.Plan));

        await Until(() => shell.Current is SplitsViewModel);
    }

    [Fact] public async Task BeforeSignIn_TheLinkWaits_ForTheFirstSignedInScreen()
    {
        var shell = _sp.GetRequiredService<MainViewModel>();
        Assert.IsType<LoginViewModel>(shell.Current);

        Assert.True(shell.OpenLink(WidgetLink.For(WidgetTarget.Body)));
        Assert.IsType<LoginViewModel>(shell.Current);

        Assert.True((await _sp.GetRequiredService<IAccountService>().RegisterAsync("alice", "alice@example.com", "correct horse 1")).Success);
        shell.ShowHome();

        Assert.IsType<ProfileViewModel>(shell.Current);
        shell.ShowHome();
        Assert.IsType<HomeViewModel>(shell.Current);
    }
}
