using System;
using System.Collections.Generic;
using GymApp.Core;

namespace GymApp.App.Views;

/// <summary>One arc of the macro ring: the centre line of its stroke, in degrees clockwise from 12 o'clock.</summary>
public record MacroArc(Macro Macro, double StartDegrees, double SweepDegrees);

/// <summary>The geometry of <see cref="MacroRing"/> (design "C", Phase 27), apart from the drawing so it can be tested: a thin ring
/// in a 200-unit box (radius 82, stroke 12) of up to three round-ended arcs with a visible gap of 6 between them. The round
/// caps add half a stroke at each end of an arc, so the gap along the circle is the visible gap plus one stroke.</summary>
public static class MacroRingGeometry
{
    public const double Box = 200, Radius = 82, Stroke = 12, VisibleGap = 6;
    public static double Circumference => 2 * Math.PI * Radius;

    /// <summary>The arcs for these percentages (protein, carbs, fat; they should add up to 100). A macro at 0 gets none. One macro
    /// alone gets a single arc covering the full circle (<see cref="IsFullCircle"/>), with no gap.</summary>
    public static IReadOnlyList<MacroArc> Arcs(int proteinPercent, int carbsPercent, int fatPercent)
    {
        var shares = new[] { (Macro.Protein, proteinPercent), (Macro.Carbs, carbsPercent), (Macro.Fat, fatPercent) };
        var present = Array.FindAll(shares, s => s.Item2 > 0);
        var arcs = new List<MacroArc>();
        if (present.Length == 0) return arcs;
        if (present.Length == 1)
        {
            arcs.Add(new MacroArc(present[0].Item1, 0, 360));
            return arcs;
        }

        var total = 0;
        foreach (var s in present) total += s.Item2;
        var gapAlong = VisibleGap + Stroke;                          // between the arcs' centre lines' ends
        var at = 0.0;                                                // slot start, in units along the circle
        foreach (var (macro, percent) in present)
        {
            var slot = Circumference * percent / total;
            var length = Math.Max(slot - gapAlong, 0.01);            // a tiny share is a dot, never negative
            var start = at + (slot - length) / 2;                    // centred in its slot
            arcs.Add(new MacroArc(macro, start / Circumference * 360, length / Circumference * 360));
            at += slot;
        }
        return arcs;
    }

    public static bool IsFullCircle(IReadOnlyList<MacroArc> arcs) => arcs.Count == 1 && arcs[0].SweepDegrees >= 360;
}
