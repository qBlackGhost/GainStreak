using GymApp.Core;

namespace GymApp.Tests;

public class GymCacheTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact] public void NeverFetched_IsStale() => Assert.True(GymCache.IsStale(null, Now));
    [Fact] public void FetchedToday_IsFresh() => Assert.False(GymCache.IsStale(Now.AddHours(-1), Now));
    [Fact] public void Exactly30Days_IsStillFresh() => Assert.False(GymCache.IsStale(Now.AddDays(-30), Now));
    [Fact] public void Over30Days_IsStale() => Assert.True(GymCache.IsStale(Now.AddDays(-30).AddMinutes(-1), Now));
}
