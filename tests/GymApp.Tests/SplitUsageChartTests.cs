using GymApp.Desktop.ViewModels;

namespace GymApp.Tests;

public class SplitUsageChartTests
{
    private static readonly (string, int, bool)[] Sample =
    [
        ("Push / Pull / Legs", 34, true), ("Upper / Lower", 24, false), ("Full Body", 16, false),
        ("PPL + Upper / Lower", 14, false), ("Bro Split", 8, false), ("Custom splits", 4, false)
    ];

    [Fact] public void SmallShares_GoIntoOther_Last_WithTheirNames()
    {
        var slices = SplitUsageChart.Build(Sample, 100);

        Assert.Equal(["Push / Pull / Legs", "Upper / Lower", "Full Body", "PPL + Upper / Lower", "Other"], slices.Select(s => s.Name));
        var other = slices[^1];
        Assert.Equal((12.0, "12%", "Bro Split · Custom splits"), (other.Percent, other.PercentText, other.Detail));
        Assert.All(slices.Take(slices.Count - 1), s => Assert.Null(s.Detail));
    }

    [Fact] public void ThisSplit_KeepsItsSlice_AndTheAccentTone_EvenWhenSmall()
    {
        var sample = Sample.Select(s => (s.Item1, s.Item2, s.Item1 == "Bro Split")).ToList();
        var slices = SplitUsageChart.Build(sample, 100);

        var mine = Assert.Single(slices, s => s.IsThis);
        Assert.Equal(("Bro Split", 0), (mine.Name, mine.Tone));
        Assert.Equal([1, 2, 3, 4, 5], slices.Where(s => !s.IsThis).Select(s => s.Tone));
        Assert.Contains(slices, s => s.Name == "Custom splits");   // the only other small one: no "Other" for one slice
        Assert.DoesNotContain(slices, s => s.Name == "Other");
    }

    [Fact] public void Percents_AreOfEveryoneWithASplit()
    {
        var slices = SplitUsageChart.Build([("Upper / Lower", 1, true), ("Custom splits", 2, false)], 3);
        Assert.Equal(["67%", "33%"], slices.Select(s => s.PercentText));
    }
}
