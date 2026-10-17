using GymApp.Core;

namespace GymApp.Tests;

public class CheckInRuleTests
{
    private static readonly DateOnly Today = new(2026, 10, 17);

    private static SetEntry W(int n, int reps = 8, double kg = 60) => new(Today, n, SetKind.Working, reps, kg);
    private static SetEntry Wu(int n, int reps = 10, double kg = 20) => new(Today, n, SetKind.Warmup, reps, kg);

    private static CheckInExercise Filled(int id, int workingSets = 3) =>
        new(id, workingSets, Enumerable.Range(1, workingSets).Select(n => W(n)).ToList());

    private static CheckInExercise Empty(int id, int workingSets = 3) => new(id, workingSets, []);

    [Fact] public void NoExercises_CantLog()
    {
        var status = CheckInRule.Evaluate([]);
        Assert.Equal(new CheckInStatus(0, 0, 0, false), status);
    }

    [Fact] public void OneExercise_Empty_CantLog() =>
        Assert.False(CheckInRule.Evaluate([Empty(1)]).CanLog);

    [Fact] public void OneExercise_Filled_CanLog()
    {
        var status = CheckInRule.Evaluate([Filled(1)]);
        Assert.Equal(new CheckInStatus(1, 1, 1, true), status);
    }

    [Fact] public void ThreeExercises_OneFilled_CantLog()
    {
        var status = CheckInRule.Evaluate([Filled(1), Empty(2), Empty(3)]);
        Assert.Equal(new CheckInStatus(1, 2, 3, false), status);
    }

    [Fact] public void ThreeExercises_TwoFilled_CanLog()
    {
        var status = CheckInRule.Evaluate([Filled(1), Filled(2), Empty(3)]);
        Assert.Equal(new CheckInStatus(2, 2, 3, true), status);
    }

    [Fact] public void FourExercises_ExactlyHalf_CanLog()
    {
        var status = CheckInRule.Evaluate([Filled(1), Empty(2), Filled(3), Empty(4)]);
        Assert.Equal(new CheckInStatus(2, 2, 4, true), status);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    public void Required_IsHalfRoundedUp(int total, int required) =>
        Assert.Equal(required, CheckInRule.RequiredFor(total));

    [Fact] public void WarmupsAlone_DontCount()
    {
        var ex = new CheckInExercise(1, 3, [Wu(1), Wu(2), Wu(3), Wu(4)]);
        Assert.False(CheckInRule.IsFilled(ex));
    }

    [Fact] public void FewerWorkingSetsThanPlanned_DontCount()
    {
        var ex = new CheckInExercise(1, 3, [Wu(1), W(1), W(2)]);
        Assert.False(CheckInRule.IsFilled(ex));
    }

    [Fact] public void MoreWorkingSetsThanPlanned_StillCount()
    {
        var ex = new CheckInExercise(1, 3, [W(1), W(2), W(3), W(4)]);
        Assert.True(CheckInRule.IsFilled(ex));
    }

    [Fact] public void BodyweightSets_AtZeroKg_Count()
    {
        var ex = new CheckInExercise(1, 2, [W(1, kg: 0), W(2, kg: 0)]);
        Assert.True(CheckInRule.IsFilled(ex));
    }
}
