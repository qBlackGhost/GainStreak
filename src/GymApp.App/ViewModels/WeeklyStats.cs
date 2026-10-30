using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GymApp.Core;

namespace GymApp.App.ViewModels;

/// <summary>One bar of "Weekly stats": the week's heaviest working set. A week with no sets has no bar;
/// <see cref="IsPr"/> marks a week in which a PR was set. Height is in chart pixels.</summary>
public record WeekBar(string Label, string Weight, double Height, bool IsPr, bool HasSet);

/// <summary>One page of weeks, oldest first, and whether there are older or newer pages.</summary>
public record WeekPage(IReadOnlyList<WeekBar> Bars, string RangeText, bool HasOlder, bool HasNewer);

/// <summary>The exercise page's "Weekly stats": the best set of each week, a page of weeks at a time. Page 0 ends
/// with this week; each page further back is the weeks before it. Pure, so the rules are easy to test.</summary>
public static class WeeklyStats
{
    public const int WeeksPerPage = 6;

    /// <summary>The Monday the week of <paramref name="date"/> starts on.</summary>
    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>The number of pages from the first week with a working set to this week (at least one).</summary>
    public static int PageCount(IEnumerable<SetEntry> sets, DateOnly today)
    {
        var dates = sets.Where(s => s.Kind == SetKind.Working).Select(s => s.Date).ToList();
        if (dates.Count == 0) return 1;
        var weeks = (WeekStart(today).DayNumber - WeekStart(dates.Min()).DayNumber) / 7 + 1;
        return Math.Max(1, (weeks + WeeksPerPage - 1) / WeeksPerPage);
    }

    public static WeekPage Build(IEnumerable<SetEntry> sets, IEnumerable<PrEntry> prs, DateOnly today, int page, double chartHeight)
    {
        var working = sets.Where(s => s.Kind == SetKind.Working).ToList();
        var pages = PageCount(working, today);
        page = Math.Clamp(page, 0, pages - 1);

        var last = WeekStart(today).AddDays(-7 * WeeksPerPage * page);
        var weeks = Enumerable.Range(0, WeeksPerPage).Select(i => last.AddDays(-7 * (WeeksPerPage - 1 - i))).ToList();
        var prWeeks = prs.Where(p => p.Kind != PrEntryKind.Manual).Select(p => WeekStart(p.Date)).ToHashSet();
        var best = weeks.Select(w => working.Where(s => WeekStart(s.Date) == w)
            .OrderByDescending(s => s.WeightKg).ThenByDescending(s => s.Reps).FirstOrDefault()).ToList();

        // Bars start a little under the page's lightest so the differences show, as in "Top set per session"
        var shown = best.OfType<SetEntry>().ToList();
        var lo = shown.Count > 0 ? shown.Min(s => s.WeightKg) * 0.8 : 0;
        var hi = shown.Count > 0 ? Math.Max(shown.Max(s => s.WeightKg), lo + 1) : 1;

        var bars = weeks.Zip(best, (w, s) => s is null
            ? new WeekBar(Short(w), "", 0, false, false)
            : new WeekBar(Short(w), NumberParsing.FormatKg(s.WeightKg),
                Math.Max(6, Math.Round((s.WeightKg - lo) / (hi - lo) * chartHeight)), prWeeks.Contains(w), true)).ToList();
        return new WeekPage(bars, $"{Short(weeks[0])} – {Short(weeks[^1].AddDays(6))}", page < pages - 1, page > 0);
    }

    private static string Short(DateOnly d) => d.ToString("d MMM", CultureInfo.InvariantCulture);
}
