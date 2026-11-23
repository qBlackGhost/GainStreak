using GymApp.App.Views;
using GymApp.Core;

namespace GymApp.Tests;

public class MacroRingTests
{
    private static double GapDegrees => (MacroRingGeometry.VisibleGap + MacroRingGeometry.Stroke) / MacroRingGeometry.Circumference * 360;

    [Fact] public void ThreeMacros_ArcsAndGapsFillTheCircle_InOrder_WithoutOverlap()
    {
        var arcs = MacroRingGeometry.Arcs(26, 43, 31);
        Assert.Equal([Macro.Protein, Macro.Carbs, Macro.Fat], arcs.Select(a => a.Macro));
        Assert.Equal(360, arcs.Sum(a => a.SweepDegrees) + 3 * GapDegrees, 6);

        for (var i = 0; i < arcs.Count; i++)
        {
            var next = arcs[(i + 1) % arcs.Count];
            var end = arcs[i].StartDegrees + arcs[i].SweepDegrees;
            var nextStart = next.StartDegrees + (i == arcs.Count - 1 ? 360 : 0);
            Assert.Equal(GapDegrees, nextStart - end, 6);   // the gap between neighbours, also across 12 o'clock
        }
    }

    [Fact] public void TwoMacros_HaveTwoGaps_AndAMacroAtZeroDrawsNothing()
    {
        var arcs = MacroRingGeometry.Arcs(60, 0, 40);
        Assert.Equal([Macro.Protein, Macro.Fat], arcs.Select(a => a.Macro));
        Assert.Equal(360, arcs.Sum(a => a.SweepDegrees) + 2 * GapDegrees, 6);
        Assert.True(arcs[1].StartDegrees > arcs[0].StartDegrees + arcs[0].SweepDegrees);
    }

    [Fact] public void OneMacroAlone_IsAFullCircleWithNoGap()
    {
        var arcs = MacroRingGeometry.Arcs(0, 100, 0);
        var only = Assert.Single(arcs);
        Assert.Equal(Macro.Carbs, only.Macro);
        Assert.Equal((0.0, 360.0), (only.StartDegrees, only.SweepDegrees));
        Assert.True(MacroRingGeometry.IsFullCircle(arcs));
        Assert.False(MacroRingGeometry.IsFullCircle(MacroRingGeometry.Arcs(50, 50, 0)));
        Assert.Empty(MacroRingGeometry.Arcs(0, 0, 0));
    }

    [Fact] public void ATinyShare_StaysAPositiveDot_AndTheFirstArcStartsNearTwelve()
    {
        var arcs = MacroRingGeometry.Arcs(1, 59, 40);
        Assert.All(arcs, a => Assert.True(a.SweepDegrees > 0));
        Assert.True(arcs[0].StartDegrees >= 0 && arcs[0].StartDegrees < 10);
    }

    [Fact] public void TheLegendAndTheScreenReaderAgreeWithTheRingsShares()
    {
        var split = MacroSplit.From(30, 30, 10)!;
        Assert.Equal("Protein 37 percent, carbs 36 percent, fat 27 percent", split.Describe());
        var arcs = MacroRingGeometry.Arcs(split.ProteinPercent, split.CarbsPercent, split.FatPercent);
        Assert.True(arcs[0].SweepDegrees > arcs[2].SweepDegrees);
        Assert.Equal(37, split.PercentOf(Macro.Protein));
    }
}
