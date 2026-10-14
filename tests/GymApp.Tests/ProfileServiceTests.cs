using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;

namespace GymApp.Tests;

public class ProfileServiceTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 14);

    private readonly TestDb _db = new();
    private readonly ProfileService _svc;
    private readonly int _alice;
    private readonly int _bob;

    public ProfileServiceTests()
    {
        _svc = new ProfileService(_db);
        _alice = _db.AddUser("alice");
        _bob = _db.AddUser("bob");
    }

    public void Dispose() => _db.Dispose();

    private static ProfileInput Full(UnitSystem units = UnitSystem.Metric) =>
        new(Sex.Male, new DateOnly(1996, 1, 1), 180, ActivityLevel.Active, units);

    [Fact] public async Task NewUser_GetsAnEmptyProfile_AndNoEstimate()
    {
        var s = (await _svc.GetAsync(_alice, Today))!;
        Assert.Equal("alice", s.Username);
        Assert.Null(s.Profile.Sex);
        Assert.Null(s.CurrentWeight);
        Assert.Null(s.Maintenance.Kcal);
        Assert.Contains("sex", s.Maintenance.Reason);
    }

    [Fact] public async Task Save_ThenGet_RoundTrips()
    {
        Assert.True((await _svc.SaveAsync(_alice, Full(UnitSystem.Imperial), Today)).Success);
        var p = (await _svc.GetAsync(_alice, Today))!.Profile;
        Assert.Equal(Sex.Male, p.Sex);
        Assert.Equal(new DateOnly(1996, 1, 1), p.BirthDate);
        Assert.Equal(180, p.HeightCm);
        Assert.Equal(ActivityLevel.Active, p.ActivityLevel);
        Assert.Equal(UnitSystem.Imperial, p.Units);
    }

    [Fact] public async Task Save_Twice_UpdatesTheSameRow()
    {
        await _svc.SaveAsync(_alice, Full(), Today);
        await _svc.SaveAsync(_alice, Full() with { HeightCm = 181 }, Today);

        await using var db = _db.CreateDbContext();
        Assert.Equal(1, db.UserProfiles.Count(p => p.UserId == _alice));
        Assert.Equal(181, db.UserProfiles.Single(p => p.UserId == _alice).HeightCm);
    }

    [Fact] public async Task Save_AllowsEmptyFields() =>
        Assert.True((await _svc.SaveAsync(_alice, new ProfileInput(null, null, null, null, UnitSystem.Metric), Today)).Success);

    [Fact] public async Task Save_RejectsOutOfRangeValues()
    {
        Assert.False((await _svc.SaveAsync(_alice, Full() with { HeightCm = 90 }, Today)).Success);
        Assert.False((await _svc.SaveAsync(_alice, Full() with { HeightCm = double.NaN }, Today)).Success);
        Assert.False((await _svc.SaveAsync(_alice, Full() with { ActivityLevel = (ActivityLevel)99 }, Today)).Success);
        Assert.False((await _svc.SaveAsync(_alice, Full() with { BirthDate = Today.AddDays(1) }, Today)).Success);
        Assert.False((await _svc.SaveAsync(_alice, Full() with { BirthDate = new DateOnly(2020, 1, 1) }, Today)).Success);
    }

    [Fact] public async Task LogWeight_SecondWeighInTodayReplacesTheFirst()
    {
        Assert.True((await _svc.LogWeightAsync(_alice, 80, Today)).Success);
        Assert.True((await _svc.LogWeightAsync(_alice, 79.5, Today)).Success);

        var s = (await _svc.GetAsync(_alice, Today))!;
        Assert.Single(s.RecentWeights);
        Assert.Equal(79.5, s.CurrentWeight!.WeightKg);
    }

    [Fact] public async Task LogWeight_CurrentIsTheLatestDay()
    {
        await _svc.LogWeightAsync(_alice, 82, Today.AddDays(-2));
        await _svc.LogWeightAsync(_alice, 81, Today);

        var s = (await _svc.GetAsync(_alice, Today))!;
        Assert.Equal(81, s.CurrentWeight!.WeightKg);
        Assert.Equal(new[] { 81.0, 82.0 }, s.RecentWeights.Select(w => w.WeightKg));
    }

    [Theory]
    [InlineData(29)]
    [InlineData(351)]
    [InlineData(double.NaN)]
    public async Task LogWeight_RejectsOutOfRange(double kg) =>
        Assert.False((await _svc.LogWeightAsync(_alice, kg, Today)).Success);

    [Fact] public async Task DeleteWeight_OnlyYourOwn()
    {
        var mine = (await _svc.LogWeightAsync(_alice, 80, Today)).Entry!;
        Assert.False(await _svc.DeleteWeightAsync(_bob, mine.Id));
        Assert.True(await _svc.DeleteWeightAsync(_alice, mine.Id));
        Assert.Null((await _svc.GetAsync(_alice, Today))!.CurrentWeight);
    }

    [Fact] public async Task Estimate_WithEverything_GivesMaintenance()
    {
        await _svc.SaveAsync(_alice, Full(), Today);   // male, 30, 180 cm, Active
        await _svc.LogWeightAsync(_alice, 80, Today);

        var m = (await _svc.GetAsync(_alice, Today))!.Maintenance;
        Assert.Equal(2760, m.Kcal);
        Assert.Equal(1780, m.Bmr);
        Assert.Equal(1.55, m.ActivityFactor);
    }

    [Fact] public void Estimate_UnderAdultAge_GivesNoNumber()
    {
        var profile = new UserProfile { Sex = Sex.Female, BirthDate = new DateOnly(2010, 1, 1), HeightCm = 160, ActivityLevel = ActivityLevel.LessActive };
        var m = ProfileService.Estimate(profile, 55, Today);   // 16
        Assert.Null(m.Kcal);
        Assert.Contains("18", m.Reason);
    }

    [Fact] public void Estimate_NamesWhatIsMissing()
    {
        var profile = new UserProfile { Sex = Sex.Male, BirthDate = new DateOnly(1996, 1, 1), HeightCm = 180 };
        var m = ProfileService.Estimate(profile, null, Today);
        Assert.Equal("Add your weight and activity level to see your maintenance calories.", m.Reason);
    }

    [Fact] public async Task Summary_ShowsTheSelectedSplit()
    {
        await using (var db = _db.CreateDbContext())
        {
            var u = db.Users.Single(x => x.Id == _alice);
            u.SelectedSplitId = 1;      // Push / Pull / Legs
            u.SelectedVariantId = 1;    // 6 days / week
            await db.SaveChangesAsync();
        }
        Assert.Equal("Push / Pull / Legs (6 days / week)", (await _svc.GetAsync(_alice, Today))!.SplitName);
    }
}
