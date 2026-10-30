using GymApp.App.ViewModels;
using GymApp.Core;

namespace GymApp.Tests;

public class WeeklyStatsTests
{
    private static readonly DateOnly Today = new(2026, 10, 29);   // a Thursday

    private static SetEntry Working(DateOnly date, double kg, int reps = 8) => new(date, 1, SetKind.Working, reps, kg);
    private static PrEntry Pr(DateOnly date, double kg) => new(date, kg, 8, kg, PrEntryKind.Pr);

    [Fact] public void WeekStart_IsTheMonday()
    {
        Assert.Equal(new DateOnly(2026, 10, 26), WeeklyStats.WeekStart(Today));
        Assert.Equal(new DateOnly(2026, 10, 26), WeeklyStats.WeekStart(new DateOnly(2026, 11, 1)));   // Sunday
        Assert.Equal(new DateOnly(2026, 10, 26), WeeklyStats.WeekStart(new DateOnly(2026, 10, 26)));
    }

    [Fact] public void FirstPage_EndsWithThisWeek_AndShowsEachWeeksHeaviestSet()
    {
        var sets = new[]
        {
            Working(new DateOnly(2026, 10, 27), 80), Working(new DateOnly(2026, 10, 29), 82.5),
            Working(new DateOnly(2026, 10, 20), 77.5), Working(new DateOnly(2026, 10, 20), 75, 10)
        };
        var page = WeeklyStats.Build(sets, [], Today, 0, 100);

        Assert.Equal(6, page.Bars.Count);
        Assert.Equal("26 Oct", page.Bars[^1].Label);
        Assert.Equal("82.5", page.Bars[^1].Weight);
        Assert.Equal("77.5", page.Bars[^2].Weight);
        Assert.Equal("21 Sep – 1 Nov", page.RangeText);
        Assert.False(page.HasNewer);
    }

    [Fact] public void WeeksWithoutSets_HaveNoBar_AndWarmupsDontCount()
    {
        var sets = new[] { Working(new DateOnly(2026, 10, 27), 80), new SetEntry(new DateOnly(2026, 10, 13), 1, SetKind.Warmup, 10, 40) };
        var page = WeeklyStats.Build(sets, [], Today, 0, 100);

        Assert.False(page.Bars[^3].HasSet);
        Assert.Equal(0, page.Bars[^3].Height);
        Assert.True(page.Bars[^1].HasSet);
    }

    [Fact] public void PrWeeks_AreMarked_ButEditedPrsAreNot()
    {
        var sets = new[] { Working(new DateOnly(2026, 10, 20), 80), Working(new DateOnly(2026, 10, 27), 80) };
        var prs = new[] { Pr(new DateOnly(2026, 10, 21), 80), new PrEntry(new DateOnly(2026, 10, 28), 90, 1, 90, PrEntryKind.Manual) };
        var page = WeeklyStats.Build(sets, prs, Today, 0, 100);

        Assert.True(page.Bars[^2].IsPr);
        Assert.False(page.Bars[^1].IsPr);
    }

    [Fact] public void OlderPages_GoBackSixWeeksAtATime()
    {
        var sets = new[] { Working(new DateOnly(2026, 8, 3), 70), Working(new DateOnly(2026, 10, 27), 80) };

        Assert.Equal(3, WeeklyStats.PageCount(sets, Today));   // 13 weeks
        var first = WeeklyStats.Build(sets, [], Today, 0, 100);
        var second = WeeklyStats.Build(sets, [], Today, 1, 100);
        var oldest = WeeklyStats.Build(sets, [], Today, 2, 100);

        Assert.True(first.HasOlder);
        Assert.Equal(("14 Sep", true, true), (second.Bars[^1].Label, second.HasOlder, second.HasNewer));
        Assert.Equal((false, true), (oldest.HasOlder, oldest.HasNewer));
        Assert.Equal("70", oldest.Bars.Single(b => b.HasSet).Weight);
    }

    [Fact] public void APageBeyondTheEnds_IsClamped()
    {
        var sets = new[] { Working(new DateOnly(2026, 10, 27), 80) };

        Assert.Equal("26 Oct", WeeklyStats.Build(sets, [], Today, 5, 100).Bars[^1].Label);
        Assert.Equal("26 Oct", WeeklyStats.Build(sets, [], Today, -1, 100).Bars[^1].Label);
    }
}
