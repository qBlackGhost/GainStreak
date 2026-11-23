using GymApp.Core;

namespace GymApp.Tests;

public class WeightChartModelTests
{
    private static readonly DateOnly Today = new(2026, 11, 23);   // a Monday

    private static (DateOnly, double)[] Logs() =>
    [
        (Today, 80.1), (Today.AddDays(-2), 80.5), (Today.AddDays(-6), 81.0),   // this week
        (Today.AddDays(-9), 81.4), (Today.AddDays(-12), 81.8),                 // the week before
        (Today.AddDays(-60), 84.0),
    ];

    [Fact] public void AWeek_HasSevenDays_EndingToday_WithEachDaysWeighIn_AndTheAverage()
    {
        var w = WeightChartModel.Window(Logs(), WeightRange.Week, Today, 0);
        Assert.Equal((Today.AddDays(-6), Today), (w.From, w.To));
        Assert.Equal(7, w.Slots.Count);
        Assert.Equal([81.0, null, null, null, 80.5, null, 80.1], w.Slots.Select(s => s.Value));
        Assert.Equal(80.5, w.Average);
        Assert.Equal((79.0, 82.0), (w.Low, w.High));   // 80.1 to 81.0 with half a kilo of room, to whole kilos
        Assert.True(w.HasOlder);
        Assert.False(w.HasNewer);
        Assert.Equal(["T", "W", "T", "F", "S", "S", "M"], WeightChartModel.AxisLabels(w));
    }

    [Fact] public void SwipingBack_ShowsTheWeekBefore_AndAnEmptyWeekKeepsAScale()
    {
        var w = WeightChartModel.Window(Logs(), WeightRange.Week, Today, 1);
        Assert.Equal((Today.AddDays(-13), Today.AddDays(-7)), (w.From, w.To));
        Assert.Equal(81.6, w.Average);
        Assert.True(w.HasNewer);

        var empty = WeightChartModel.Window(Logs(), WeightRange.Week, Today, 3);
        Assert.Null(empty.Average);
        Assert.All(empty.Slots, s => Assert.Null(s.Value));
        Assert.True(empty.High > empty.Low);
        Assert.True(empty.HasOlder);   // the weigh-in 60 days ago is still behind it
    }

    [Fact] public void SixMonths_AreWeeklyAverages()
    {
        var w = WeightChartModel.Window(Logs(), WeightRange.SixMonths, Today, 0);
        Assert.Equal(26, w.Slots.Count);
        Assert.Equal(Today, w.To);
        Assert.Equal(Today.AddDays(-181), w.From);
        Assert.Equal(80.5, w.Slots[^1].Value);   // the last 7 days: 81.0, 80.5, 80.1
        Assert.Equal(("WEEKLY AVERAGE", "80.5", "17–23 Nov"), WeightChartModel.Header(w, 25));
    }

    [Fact] public void Holding_ReadsTheSlotUnderTheThumb()
    {
        var w = WeightChartModel.Window(Logs(), WeightRange.Week, Today, 0);
        Assert.Equal(0, WeightChartModel.SlotAt(WeightRange.Week, 0));
        Assert.Equal(3, WeightChartModel.SlotAt(WeightRange.Week, 0.5));
        Assert.Equal(6, WeightChartModel.SlotAt(WeightRange.Week, 1.2));
        Assert.Equal(("WEIGHT", "80.1", "Mon 23 Nov"), WeightChartModel.Header(w, 6));
        Assert.Equal(("NO WEIGH-IN", null, "Sun 22 Nov"), WeightChartModel.Header(w, 5));
        Assert.Equal(("AVERAGE", "80.5", "17–23 Nov"), WeightChartModel.Header(w));
    }

    [Fact] public void AMonth_LabelsEveryWeek_FromToday()
    {
        var w = WeightChartModel.Window(Logs(), WeightRange.Month, Today, 0);
        var labels = WeightChartModel.AxisLabels(w);
        Assert.Equal("23 Nov", labels[^1]);
        Assert.Equal("16 Nov", labels[^8]);
        Assert.Equal(5, labels.Count(l => l != ""));
    }
}
