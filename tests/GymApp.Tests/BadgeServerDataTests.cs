using GymApp.Core;
using GymApp.Services;

namespace GymApp.Tests;

/// <summary>Phase 25d on the phone: what comes from the server for badges.</summary>
public class BadgeServerDataTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly BadgeService _svc;
    private readonly int _alice;

    public BadgeServerDataTests()
    {
        _svc = new BadgeService(_db);
        _alice = _db.AddUser("alice");
    }

    public void Dispose() => _db.Dispose();

    [Fact] public async Task AcceptedInvites_EarnRecruiter_AndTheCountNeverGoesDown()
    {
        var today = new DateOnly(2027, 3, 1);
        await _svc.RecordAcceptedInvitesAsync(_alice, 2, today);
        await _svc.RecordAcceptedInvitesAsync(_alice, 1, today);   // a friend left

        var recruiter = (await _svc.GetProgressAsync(_alice, today)).Single(p => p.Id == "recruiter");

        Assert.True(recruiter.Earned);
        Assert.Equal(2, recruiter.Value);
    }

    [Fact] public async Task NoInvites_NoRecruiter()
    {
        await _svc.RecordAcceptedInvitesAsync(_alice, 0);
        Assert.False((await _svc.GetProgressAsync(_alice)).Single(p => p.Id == "recruiter").Earned);
    }

    [Theory]
    [InlineData("streak:3", "streak", 3)]
    [InlineData("monthly-2026-12:0", "monthly-2026-12", 0)]
    public void FeaturedBadge_RoundTrips(string text, string id, int tier)
    {
        Assert.Equal((id, tier), FeaturedBadge.Parse(text));
        Assert.Equal(text, FeaturedBadge.Format(id, tier));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("streak")]
    [InlineData("streak:9")]
    [InlineData(":2")]
    public void FeaturedBadge_Malformed_IsNull(string? text) => Assert.Null(FeaturedBadge.Parse(text));
}
