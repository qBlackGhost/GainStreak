using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using GymApp.Services;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

/// <summary>Phase 25b: storing badges. Real migrations on in-memory SQLite.</summary>
public class BadgeServiceTests : IDisposable
{
    // 2027-03-01 is a Monday
    private static readonly DateOnly Mon = new(2027, 3, 1);
    private static DateOnly D(int n) => Mon.AddDays(n);

    private readonly TestDb _db = new();
    private readonly BadgeService _svc;
    private readonly int _alice;

    public BadgeServiceTests()
    {
        _svc = new BadgeService(_db);
        _alice = _db.AddUser("alice");
    }

    public void Dispose() => _db.Dispose();

    private void Seed(Action<AppDbContext> change)
    {
        using var db = _db.CreateDbContext();
        change(db);
        db.SaveChanges();
    }

    private void Workouts(int from, int count, int? gymId = null) => Seed(db =>
        db.WorkoutLogs.AddRange(Enumerable.Range(from, count).Select(i => new WorkoutLog { UserId = _alice, Date = D(i), GymId = gymId })));

    private List<EarnedBadge> Stored()
    {
        using var db = _db.CreateDbContext();
        return db.EarnedBadges.Where(e => e.UserId == _alice).ToList();
    }

    [Fact] public async Task FirstCheck_StoresTheHistoryQuietly_AndFlagsTheSheetOnce()
    {
        Workouts(0, 10);

        var check = await _svc.CheckAsync(_alice, D(20));

        Assert.True(check.Backfilled);
        Assert.Empty(check.Unlocks);
        var century = Assert.Single(Stored(), e => e.BadgeId == "century");
        Assert.Equal(1, century.Tier);
        Assert.Equal(D(9), century.EarnedOn);
        Assert.All(Stored(), e => Assert.NotNull(e.SeenAt));
        Assert.Empty(await _svc.GetUnseenAsync(_alice));
        Assert.Equal(Stored().Count, await _svc.TakeHistorySheetAsync(_alice));
        Assert.Null(await _svc.TakeHistorySheetAsync(_alice));
    }

    [Fact] public async Task FirstCheck_WithNoHistory_HasNoSheet_AndTheNextBadgeIsAnUnlock()
    {
        await _svc.CheckAsync(_alice, D(0));
        Assert.Null(await _svc.TakeHistorySheetAsync(_alice));

        Workouts(0, 10);
        var check = await _svc.CheckAsync(_alice, D(10));

        Assert.False(check.Backfilled);
        Assert.Contains(check.Unlocks, u => u is { BadgeId: "century", Tier: 1 });
        Assert.Contains(await _svc.GetUnseenAsync(_alice), u => u.BadgeId == "century");
    }

    [Fact] public async Task ATierClimb_IsAnUnlock_UntilSeen()
    {
        Workouts(0, 10);
        await _svc.CheckAsync(_alice, D(100));
        Workouts(10, 40);

        var check = await _svc.CheckAsync(_alice, D(100));

        var unlock = Assert.Single(check.Unlocks, u => u.BadgeId == "century");
        Assert.Equal(2, unlock.Tier);
        Assert.Equal(D(49), unlock.EarnedOn);
        await _svc.MarkSeenAsync(_alice, ["century"]);
        Assert.DoesNotContain(await _svc.GetUnseenAsync(_alice), u => u.BadgeId == "century");
        Assert.DoesNotContain((await _svc.CheckAsync(_alice, D(100))).Unlocks, u => u.BadgeId == "century");   // nothing new
    }

    [Fact] public async Task DeletingTheLogs_KeepsTheBadge()
    {
        Workouts(0, 10);
        await _svc.CheckAsync(_alice, D(20));
        Seed(db => db.WorkoutLogs.RemoveRange(db.WorkoutLogs.Where(w => w.UserId == _alice)));

        await _svc.CheckAsync(_alice, D(20));

        Assert.Equal(1, Assert.Single(Stored(), e => e.BadgeId == "century").Tier);
        var progress = (await _svc.GetProgressAsync(_alice, D(20))).Single(p => p.Id == "century");
        Assert.True(progress.Earned);
        Assert.Equal(1, progress.Tier);
        Assert.Equal(0, progress.Value);
    }

    [Fact] public async Task AOneTimeBadge_IsStoredWithTierZero()
    {
        await _svc.CheckAsync(_alice, new DateOnly(2027, 1, 1));
        Seed(db => db.WorkoutLogs.Add(new WorkoutLog { UserId = _alice, Date = new DateOnly(2027, 1, 1) }));

        var check = await _svc.CheckAsync(_alice, new DateOnly(2027, 1, 2));

        Assert.Contains(check.Unlocks, u => u is { BadgeId: "fresh-start", Tier: 0 });
    }

    [Fact] public async Task Sets_WeighIns_AndLegs_ReachTheRules()
    {
        int legs = 0, chest = 0;
        Seed(db =>
        {
            legs = db.Exercises.First(e => e.MuscleGroup == MuscleGroup.Quads).Id;
            chest = db.Exercises.First(e => e.MuscleGroup == MuscleGroup.Chest).Id;
            db.WeightLogs.Add(new WeightLog { UserId = _alice, Date = D(0), WeightKg = 80 });
            db.SetLogs.Add(new SetLog { UserId = _alice, ExerciseId = chest, Date = D(1), SetNumber = 1, Kind = SetKind.Working, Reps = 5, WeightKg = 100 });
            foreach (var week in new[] { 0, 7, 14, 21 })
                db.SetLogs.Add(new SetLog { UserId = _alice, ExerciseId = legs, Date = D(week + 2), SetNumber = 1, Kind = SetKind.Working, Reps = 5, WeightKg = 60 });
        });

        var progress = (await _svc.GetProgressAsync(_alice, D(30))).ToDictionary(p => p.Id);

        Assert.Equal(1, progress["bodyweight-club"].Tier);        // 100 kg at 80 kg body weight
        Assert.True(progress["triple-digits"].Earned);
        Assert.Equal(1, progress["never-skip-leg-day"].Tier);     // four weeks in a row
    }

    private (int Mine, int Custom) Gyms()
    {
        int mine = 0, custom = 0;
        Seed(db =>
        {
            var city = new City { OsmType = OsmElementType.Relation, OsmId = 1, Name = "Timișoara" };
            var osm = new Gym { City = city, Name = "Iron", OsmType = OsmElementType.Node, OsmId = 42 };
            var own = new Gym { City = city, Name = "Garage", OwnerUserId = _alice };
            db.Gyms.AddRange(osm, own);
            db.SaveChanges();
            (mine, custom) = (osm.Id, own.Id);
        });
        return (mine, custom);
    }

    private static FriendCheckIn FriendAt(int gymId, DateOnly date, bool custom = false) =>
        new(7, "mara", gymId, "Iron", null, null, null, null, null, custom, date);

    [Fact] public async Task WeGoJim_FriendDaysAreKept_AfterTheCheckInsAreGone()
    {
        var (gym, custom) = Gyms();
        Workouts(0, 2, gym);
        Workouts(2, 1, custom);

        await _svc.CheckAsync(_alice, D(5), [FriendAt(gym, D(0)), FriendAt(gym, D(1)), FriendAt(gym, D(4)), FriendAt(custom, D(2), custom: true)]);
        var later = await _svc.GetProgressAsync(_alice, D(60));       // no check-ins passed any more

        var weGoJim = later.Single(p => p.Id == "we-go-jim");
        Assert.Equal(2, weGoJim.Value);
        Assert.Equal(1, weGoJim.Tier);
    }

    [Fact] public async Task WeekResults_AreStoredOncePerWeek()
    {
        await _svc.RecordWeekResultAsync(_alice, D(0), first: true);
        await _svc.RecordWeekResultAsync(_alice, D(0), first: false);   // the same week again: ignored
        await _svc.RecordWeekResultAsync(_alice, D(7), first: false);

        Assert.True(await _svc.HasWeekResultAsync(_alice, D(7)));
        Assert.False(await _svc.HasWeekResultAsync(_alice, D(14)));
        var top = (await _svc.GetProgressAsync(_alice, D(30))).Single(p => p.Id == "top-of-the-board");
        Assert.Equal(1, top.Value);
    }

    [Fact] public async Task AnotherUsersData_DoesNotCount()
    {
        var bob = _db.AddUser("bob");
        Seed(db => db.WorkoutLogs.AddRange(Enumerable.Range(0, 10).Select(i => new WorkoutLog { UserId = bob, Date = D(i) })));

        await _svc.CheckAsync(_alice, D(20));

        Assert.DoesNotContain(Stored(), e => e.BadgeId == "century");
    }

    [Fact] public async Task ShareBadges_DefaultsToOn()
    {
        Seed(db => db.UserProfiles.Add(new UserProfile { UserId = _alice }));
        using var db = _db.CreateDbContext();
        var profile = await db.UserProfiles.SingleAsync(p => p.UserId == _alice);
        Assert.True(profile.ShareBadges);
        Assert.Null(profile.FeaturedBadge);
    }
}
