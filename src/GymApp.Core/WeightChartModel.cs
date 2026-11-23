using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GymApp.Core;

/// <summary>How much time the weight chart shows at once (Phase 27, like Apple Health's W / M / 6M).</summary>
public enum WeightRange { Week, Month, SixMonths }

/// <summary>One slot of the chart: a day (week and month), or a week (six months). <see cref="Value"/> is the weigh-in, or the
/// average of the slot's weigh-ins, in the shown unit; null when there was none.</summary>
public record WeightSlot(int Index, DateOnly Start, DateOnly End, double? Value);

/// <summary>What the chart shows for one page: its slots, the average, and a y scale with some room above and below.</summary>
public record WeightWindow(WeightRange Range, DateOnly From, DateOnly To, IReadOnlyList<WeightSlot> Slots, double? Average,
    double Low, double High, bool HasOlder, bool HasNewer);

/// <summary>
/// The weight chart's maths (Phase 27). Page 0 ends today; page 1 is the window before it, and so on. A week is 7 days, a month
/// 30 days, six months 26 weeks with each week's average. Values are in the user's unit.
/// </summary>
public static class WeightChartModel
{
    public static int SlotCount(WeightRange range) => range switch { WeightRange.Week => 7, WeightRange.Month => 30, _ => 26 };
    private static int SlotDays(WeightRange range) => range == WeightRange.SixMonths ? 7 : 1;
    private static int WindowDays(WeightRange range) => SlotCount(range) * SlotDays(range);

    public static WeightWindow Window(IEnumerable<(DateOnly Date, double Value)> weighIns, WeightRange range, DateOnly today, int page)
    {
        var all = weighIns.Where(w => w.Date <= today).ToList();
        page = Math.Max(0, page);
        var days = WindowDays(range);
        var to = today.AddDays(-page * days);
        var from = to.AddDays(-(days - 1));

        var slots = new List<WeightSlot>(SlotCount(range));
        for (var i = 0; i < SlotCount(range); i++)
        {
            var start = from.AddDays(i * SlotDays(range));
            var end = start.AddDays(SlotDays(range) - 1);
            var inSlot = all.Where(w => w.Date >= start && w.Date <= end).Select(w => w.Value).ToList();
            slots.Add(new WeightSlot(i, start, end, inSlot.Count > 0 ? Math.Round(inSlot.Average(), 1) : null));
        }

        var inWindow = all.Where(w => w.Date >= from && w.Date <= to).Select(w => w.Value).ToList();
        double? average = inWindow.Count > 0 ? Math.Round(inWindow.Average(), 1) : null;

        // The scale: this window's values, else all of them, with half a unit of room and at least 2 units tall
        var scaleValues = inWindow.Count > 0 ? inWindow : all.Select(w => w.Value).ToList();
        double low, high;
        if (scaleValues.Count == 0) { low = 0; high = 2; }
        else
        {
            low = Math.Floor(scaleValues.Min() - 0.5);
            high = Math.Ceiling(scaleValues.Max() + 0.5);
            if (high - low < 2) { var mid = (high + low) / 2; low = Math.Floor(mid - 1); high = low + 2; }
        }
        return new WeightWindow(range, from, to, slots, average, low, high, all.Any(w => w.Date < from), page > 0);
    }

    /// <summary>The slot under a point <paramref name="fraction"/> of the way across the plot (0 left, 1 right).</summary>
    public static int SlotAt(WeightRange range, double fraction)
    {
        var n = SlotCount(range);
        return Math.Clamp((int)Math.Floor(Math.Clamp(fraction, 0, 0.999999) * n), 0, n - 1);
    }

    /// <summary>The header while nothing is held: "AVERAGE", the value, the window's dates.</summary>
    public static (string Label, string? Value, string Date) Header(WeightWindow w) =>
        ("AVERAGE", Format(w.Average), Dates(w.From, w.To));

    /// <summary>The header for a held slot: that day's weigh-in, or that week's average on the six-month chart.</summary>
    public static (string Label, string? Value, string Date) Header(WeightWindow w, int slot)
    {
        var s = w.Slots[Math.Clamp(slot, 0, w.Slots.Count - 1)];
        if (w.Range == WeightRange.SixMonths)
            return (s.Value is null ? "NO WEIGH-INS" : "WEEKLY AVERAGE", Format(s.Value), Dates(s.Start, s.End));
        return (s.Value is null ? "NO WEIGH-IN" : "WEIGHT", Format(s.Value), s.Start.ToString("ddd d MMM", CultureInfo.InvariantCulture));
    }

    /// <summary>The labels under the plot, one per slot, empty where none is drawn: day letters for a week, a date every 7 days
    /// for a month, the month at its first week for six months.</summary>
    public static IReadOnlyList<string> AxisLabels(WeightWindow w) => w.Slots.Select(s => w.Range switch
    {
        WeightRange.Week => s.Start.ToString("ddd", CultureInfo.InvariantCulture)[..1],
        WeightRange.Month => (w.Slots.Count - 1 - s.Index) % 7 == 0 ? s.Start.ToString("d MMM", CultureInfo.InvariantCulture) : "",
        _ => s.Index == 0 || s.Start.Month != s.Start.AddDays(-7).Month ? s.Start.ToString("MMM", CultureInfo.InvariantCulture) : "",
    }).ToList();

    private static string? Format(double? v) => v?.ToString("0.0", CultureInfo.InvariantCulture);

    private static string Dates(DateOnly from, DateOnly to) =>
        from.Year != to.Year ? $"{from.ToString("d MMM yyyy", CultureInfo.InvariantCulture)} – {to.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}"
        : from.Month != to.Month ? $"{from.ToString("d MMM", CultureInfo.InvariantCulture)} – {to.ToString("d MMM", CultureInfo.InvariantCulture)}"
        : $"{from.Day}–{to.ToString("d MMM", CultureInfo.InvariantCulture)}";
}
