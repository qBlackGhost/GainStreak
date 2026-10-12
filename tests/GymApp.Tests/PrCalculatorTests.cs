using GymApp.Core;

namespace GymApp.Tests;

public class PrCalculatorTests
{
    private static readonly DateOnly Push1 = new(2026, 10, 5);
    private static DateOnly D(int days) => Push1.AddDays(days);
    private static SetEntry W(int day, int n, double kg, int reps) => new(D(day), n, SetKind.Working, reps, kg);

    [Fact] public void Epley_Maths()
    {
        Assert.Equal(80.0, OneRepMax.Epley(60, 10), 3);
        Assert.Equal(116.667, OneRepMax.Epley(100, 5), 3);
    }

    [Fact] public void Epley_SingleRep_IsTheWeight() =>
        Assert.Equal(100.0, OneRepMax.Epley(100, 1));

    [Fact] public void FirstSet_IsTheBaseline()
    {
        var h = PrCalculator.Compute([W(0, 1, 60, 10)]);
        Assert.Equal(PrEntryKind.Baseline, h.Current!.Kind);
        Assert.Equal(60, h.Current.WeightKg);
        Assert.Equal(10, h.Current.Reps);
        Assert.Equal(80.0, h.Current.OneRepMaxKg, 3);
    }

    [Fact] public void NoSets_NoCurrent() =>
        Assert.Null(PrCalculator.Compute([]).Current);

    [Fact] public void MoreRepsAtSameWeight_IsAPr()
    {
        var h = PrCalculator.Compute([W(0, 1, 60, 10), W(7, 1, 60, 12)]);
        Assert.Equal(2, h.Entries.Count);
        Assert.Equal(PrEntryKind.Pr, h.Current!.Kind);
        Assert.Equal(12, h.Current.Reps);
    }

    [Fact] public void HeavierWeight_BeatsMoreReps()
    {
        var h = PrCalculator.Compute([W(0, 1, 60, 10), W(7, 1, 60, 12), W(14, 1, 65, 5)]);
        Assert.Equal(3, h.Entries.Count);
        Assert.Equal(65, h.Current!.WeightKg);
        Assert.Equal(5, h.Current.Reps);
    }

    [Fact] public void FewerRepsAtSameWeight_IsNotAPr()
    {
        var h = PrCalculator.Compute([W(0, 1, 60, 12), W(7, 1, 60, 10)]);
        Assert.Single(h.Entries);
        Assert.Equal(12, h.Current!.Reps);
    }

    [Fact] public void SameWeightAndReps_IsATie_NotAPr()
    {
        var h = PrCalculator.Compute([W(0, 1, 60, 10), W(7, 1, 60, 10)]);
        Assert.Single(h.Entries);
    }

    [Fact] public void LighterWeight_IsNotAPr_EvenWithMoreReps()
    {
        var h = PrCalculator.Compute([W(0, 1, 60, 10), W(7, 1, 50, 20)]);
        Assert.Equal(60, h.Current!.WeightKg);
        Assert.Single(h.Entries);
    }

    [Fact] public void Warmups_AreIgnored()
    {
        var sets = new[] { new SetEntry(D(0), 1, SetKind.Warmup, 5, 100), W(0, 2, 60, 10) };
        Assert.Equal(60, PrCalculator.Compute(sets).Current!.WeightKg);
    }

    [Fact] public void DeletingTheBestSet_FallsBackToThePreviousBest()
    {
        var all = new[] { W(0, 1, 60, 10), W(7, 1, 65, 5) };
        Assert.Equal(65, PrCalculator.Compute(all).Current!.WeightKg);
        Assert.Equal(60, PrCalculator.Compute(all.Take(1)).Current!.WeightKg);
    }

    [Fact] public void EditingASet_ChangesThePr()
    {
        var edited = new[] { W(0, 1, 60, 10), W(7, 1, 55, 5) };   // 65 was a typo for 55
        Assert.Equal(60, PrCalculator.Compute(edited).Current!.WeightKg);
    }

    [Fact] public void Adjustment_CanLowerTheCurrentBest()
    {
        var h = PrCalculator.Compute([W(0, 1, 60, 10)], [new PrAdjustment(D(1), 55, 10)]);
        Assert.Equal(PrEntryKind.Manual, h.Current!.Kind);
        Assert.Equal(55, h.Current.WeightKg);
    }

    [Fact] public void Adjustment_ThenLaterSetsAreComparedToIt()
    {
        var adj = new[] { new PrAdjustment(D(1), 55, 10) };
        var heavier = PrCalculator.Compute([W(0, 1, 60, 10), W(7, 1, 57.5, 8)], adj);
        Assert.Equal(57.5, heavier.Current!.WeightKg);
        Assert.Equal(PrEntryKind.Pr, heavier.Current.Kind);

        var moreReps = PrCalculator.Compute([W(0, 1, 60, 10), W(7, 1, 55, 12)], adj);
        Assert.Equal(12, moreReps.Current!.Reps);

        var lighter = PrCalculator.Compute([W(0, 1, 60, 10), W(7, 1, 52.5, 8)], adj);
        Assert.Equal(55, lighter.Current!.WeightKg);
    }

    [Fact] public void Adjustment_CanOverrideTheOneRepMax()
    {
        var h = PrCalculator.Compute([W(0, 1, 60, 10)], [new PrAdjustment(D(1), 60, 10, 90)]);
        Assert.Equal(90, h.Current!.OneRepMaxKg);
    }

    [Fact] public void LastSession_ReturnsTheMostRecentEarlierDay()
    {
        var sets = new[] { W(0, 1, 60, 10), W(0, 2, 60, 8), W(7, 1, 62.5, 6) };
        var last = PrCalculator.LastSession(sets, D(14));
        Assert.Single(last);
        Assert.Equal(62.5, last[0].WeightKg);
        Assert.Equal(2, PrCalculator.LastSession(sets, D(7)).Count);   // before day 7 -> day 0
    }

    [Fact] public void LastSession_IgnoresTodayAndTheFuture() =>
        Assert.Empty(PrCalculator.LastSession([W(0, 1, 60, 10)], D(0)));
}