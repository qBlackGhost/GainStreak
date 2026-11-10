using GymApp.Core;

namespace GymApp.Tests;

/// <summary>Phase 20: the sync identifiers and the reconcile rule.</summary>
public class HealthPlanTests
{
    private static readonly DateOnly Day = new(2026, 11, 9);
    private static readonly DateTime Now = new(2026, 11, 9, 20, 0, 0);

    private static HealthItem Weight(int user, double kg, long version = 0) =>
        new(HealthPlan.WeightId(user, Day), HealthKind.Weight, Day.ToDateTime(new TimeOnly(8, 0)), Day.ToDateTime(new TimeOnly(8, 0)), kg, Version: version);

    [Fact] public void Ids_NameTheAccountKindAndDay()
    {
        Assert.Equal("gainstreak-u3-workout-2026-11-09", HealthPlan.WorkoutId(3, Day));
        Assert.Equal("gainstreak-u3-cardio-2026-11-09-17", HealthPlan.CardioId(3, Day, 17));
        Assert.Equal("gainstreak-u3-weight-2026-11-09", HealthPlan.WeightId(3, Day));
    }

    [Fact] public void IsOwn_OnlyThisAccount()
    {
        Assert.True(HealthPlan.IsOwn("gainstreak-u3-weight-2026-11-09", 3));
        Assert.False(HealthPlan.IsOwn("gainstreak-u31-weight-2026-11-09", 3));   // u3 is not a prefix of u31
        Assert.False(HealthPlan.IsOwn("gainstreak-u4-weight-2026-11-09", 3));
        Assert.False(HealthPlan.IsOwn(null, 3));
        Assert.False(HealthPlan.IsOwn("withings-123", 3));
    }

    [Fact] public void Diff_New_IsSaved_AtNowsTicks()
    {
        var c = HealthPlan.Diff(3, [Weight(3, 80)], [], Now);
        Assert.Equal(Now.Ticks, Assert.Single(c.Save).Version);
        Assert.Empty(c.Delete);
    }

    [Fact] public void Diff_Unchanged_DoesNothing() =>
        Assert.True(HealthPlan.Diff(3, [Weight(3, 80)], [Weight(3, 80.001, version: 5)], Now).IsEmpty);

    [Fact] public void Diff_Changed_IsSaved_AboveTheStoredVersion()
    {
        var stored = Now.Ticks + 100;   // the phone's clock went back since
        var c = HealthPlan.Diff(3, [Weight(3, 79)], [Weight(3, 80, version: stored)], Now);
        Assert.Equal(stored + 1, Assert.Single(c.Save).Version);
    }

    [Fact] public void Diff_GoneFromTheApp_IsDeleted()
    {
        var c = HealthPlan.Diff(3, [], [Weight(3, 80, version: 5)], Now);
        Assert.Empty(c.Save);
        Assert.Equal(HealthPlan.WeightId(3, Day), Assert.Single(c.Delete).SyncId);
    }

    [Fact] public void Diff_NeverTouchesOtherAccountsOrOtherApps()
    {
        var other = Weight(4, 70, version: 5);
        var scale = new HealthItem("withings-1", HealthKind.Weight, Now, Now, 81, Version: 1);
        Assert.True(HealthPlan.Diff(3, [], [other, scale], Now).IsEmpty);
    }

    [Fact] public void Diff_Milliseconds_DontCount()
    {
        var at = Day.ToDateTime(new TimeOnly(18, 0));
        var stored = new HealthItem(HealthPlan.WorkoutId(3, Day), HealthKind.StrengthWorkout, at, at.AddHours(1), Title: "Push", Version: 5);
        Assert.True(HealthPlan.Diff(3, [stored with { Start = at.AddMilliseconds(123), Version = 0 }], [stored], Now).IsEmpty);
    }

    [Fact] public void Diff_WorkoutTimesOrTitle_CountAsAChange()
    {
        var at = Day.ToDateTime(new TimeOnly(18, 0));
        var stored = new HealthItem(HealthPlan.WorkoutId(3, Day), HealthKind.StrengthWorkout, at, at.AddHours(1), Title: "Push", Version: 5);
        Assert.Single(HealthPlan.Diff(3, [stored with { End = at.AddHours(1.5), Version = 0 }], [stored], Now).Save);
        Assert.Single(HealthPlan.Diff(3, [stored with { Title = "Pull", Version = 0 }], [stored], Now).Save);
        Assert.True(HealthPlan.Diff(3, [stored with { Version = 0 }], [stored], Now).IsEmpty);
    }
}
