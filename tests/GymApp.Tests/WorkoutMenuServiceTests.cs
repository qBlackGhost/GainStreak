using GymApp.Data.Models;
using GymApp.Services;

namespace GymApp.Tests;

public class WorkoutMenuServiceTests : IDisposable
{
    private static readonly DateOnly Mon = new(2026, 10, 5);
    private static DateOnly D(int n) => Mon.AddDays(n);
    private const int Ppl = 1, PplPush = 1, PplPull = 2;

    private readonly TestDb _db = new();
    private readonly WorkoutMenuService _svc;
    private readonly int _alice;
    private readonly int _bob;

    public WorkoutMenuServiceTests()
    {
        _svc = new WorkoutMenuService(_db);
        _alice = _db.AddUser("alice");
        _bob = _db.AddUser("bob");
    }

    public void Dispose() => _db.Dispose();

    private void Select(int userId, int splitId, int? variantId = null, DayOfWeek? start = null)
    {
        using var db = _db.CreateDbContext();
        var u = db.Users.Find(userId)!;
        u.SelectedSplitId = splitId;
        u.SelectedVariantId = variantId;
        u.SplitStartDay = start;
        db.SaveChanges();
    }

    private static string? TodayName(WorkoutMenu? menu) => menu!.Days.SingleOrDefault(d => d.IsToday)?.Name;

    [Fact] public async Task NoSplitSelected_ReturnsNull() =>
        Assert.Null(await _svc.GetAsync(_alice, D(0)));

    [Fact] public async Task Ppl_ListsEveryDay_WithItsWeekdays()
    {
        Select(_alice, Ppl, variantId: 1, start: DayOfWeek.Monday);

        var menu = (await _svc.GetAsync(_alice, D(0)))!;

        Assert.Equal("Push / Pull / Legs", menu.SplitName);
        Assert.Equal(new[] { "Push", "Pull", "Legs" }, menu.Days.Select(d => d.Name));
        Assert.Equal(new[] { "Mon · Fri", "Tue · Sat", "Wed · Sun" }, menu.Days.Select(d => d.Weekdays));
    }

    [Fact] public async Task TheDayDueTodayIsMarked()
    {
        Select(_alice, Ppl, variantId: 1, start: DayOfWeek.Monday);

        Assert.Equal("Push", TodayName(await _svc.GetAsync(_alice, D(0))));   // Mon
        Assert.Equal("Legs", TodayName(await _svc.GetAsync(_alice, D(2))));   // Wed
        Assert.Equal("Push", TodayName(await _svc.GetAsync(_alice, D(4))));   // Fri: the rotation restarts after the rest day
    }

    [Fact] public async Task OnARestDay_NothingIsMarked_AndItSaysSo()
    {
        Select(_alice, Ppl, variantId: 1, start: DayOfWeek.Monday);

        var menu = (await _svc.GetAsync(_alice, D(3)))!;   // Thursday

        Assert.True(menu.IsRestToday);
        Assert.DoesNotContain(menu.Days, d => d.IsToday);
        Assert.False((await _svc.GetAsync(_alice, D(0)))!.IsRestToday);
    }

    [Fact] public async Task TheStartDay_ShiftsTheWeek()
    {
        Select(_alice, Ppl, variantId: 1, start: DayOfWeek.Wednesday);

        var menu = (await _svc.GetAsync(_alice, D(0)))!;   // Monday is Pull in this week

        Assert.Equal(new[] { "Wed · Sun", "Mon · Thu", "Tue · Fri" }, menu.Days.Select(d => d.Weekdays));
        Assert.Equal("Pull", TodayName(menu));
    }

    [Fact] public async Task NoSavedVariant_FallsBackToTheDefaultAndMonday()
    {
        Select(_alice, Ppl);

        var menu = (await _svc.GetAsync(_alice, D(0)))!;

        Assert.Equal(new[] { "Mon · Fri", "Tue · Sat", "Wed · Sun" }, menu.Days.Select(d => d.Weekdays));
    }

    [Fact] public async Task ExerciseCounts_AreThisUsersOnly()
    {
        Select(_alice, Ppl, variantId: 1, start: DayOfWeek.Monday);
        var days = new DayExerciseService(_db);
        await days.AddAsync(_alice, PplPush, 1);
        await days.AddAsync(_alice, PplPush, 2);
        await days.AddAsync(_bob, PplPush, 3);

        var menu = (await _svc.GetAsync(_alice, D(0)))!;

        Assert.Equal(2, menu.Days.Single(d => d.SplitDayId == PplPush).ExerciseCount);
        Assert.Equal(0, menu.Days.Single(d => d.SplitDayId == PplPull).ExerciseCount);
    }

    [Fact] public async Task CustomSplit_UsesItsOwnWeekdays()
    {
        int splitId;
        await using (var db = _db.CreateDbContext())
        {
            var split = new WorkoutSplit { Name = "Mine", OwnerUserId = _alice };
            split.Days.Add(new SplitDay { Name = "Chest", Order = 1, Weekday = DayOfWeek.Monday });
            split.Days.Add(new SplitDay { Name = "Back", Order = 2, Weekday = DayOfWeek.Wednesday });
            split.Days.Add(new SplitDay { Name = "Legs", Order = 3, Weekday = DayOfWeek.Friday });
            db.WorkoutSplits.Add(split);
            await db.SaveChangesAsync();
            splitId = split.Id;
        }
        Select(_alice, splitId);

        var menu = (await _svc.GetAsync(_alice, D(2)))!;   // Wednesday

        Assert.Equal(new[] { "Chest", "Back", "Legs" }, menu.Days.Select(d => d.Name));
        Assert.Equal(new[] { "Mon", "Wed", "Fri" }, menu.Days.Select(d => d.Weekdays));
        Assert.Equal("Back", TodayName(menu));
        Assert.True((await _svc.GetAsync(_alice, D(1)))!.IsRestToday);   // Tuesday
    }
}