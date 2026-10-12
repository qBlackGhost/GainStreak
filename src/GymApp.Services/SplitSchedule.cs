using System;
using System.Collections.Generic;
using System.Linq;
using GymApp.Core;
using GymApp.Data.Models;

namespace GymApp.Services;

/// <summary>A split resolved into a Monday-first week. Days[i] is the split day for RotationIndex i.</summary>
public record ResolvedSchedule(IReadOnlyList<ScheduleSlot> Week, IReadOnlyList<SplitDay> Days);

public static class SplitSchedule
{
    /// <summary>
    /// Builds the user's current week from their selection. Needs split.Days (and split.Variants for presets)
    /// loaded. A preset with no saved variant falls back to its default variant and Monday.
    /// Returns null when there is no usable schedule.
    /// </summary>
    public static ResolvedSchedule? Resolve(WorkoutSplit split, User user)
    {
        var days = split.Days.OrderBy(d => d.Order).ToList();
        if (days.Count == 0) return null;

        if (split.OwnerUserId is null)
        {
            var variant = split.Variants.FirstOrDefault(v => v.Id == user.SelectedVariantId)
                          ?? split.Variants.FirstOrDefault(v => v.IsDefault)
                          ?? split.Variants.FirstOrDefault();
            return variant is null
                ? null
                : new ResolvedSchedule(
                    WeekSchedule.Build(variant.Pattern, user.SplitStartDay ?? DayOfWeek.Monday, days.Count), days);
        }

        // Custom split: the user picked the weekdays directly
        var trained = days.Where(d => d.Weekday is not null).ToList();
        return trained.Count == 0
            ? null
            : new ResolvedSchedule(WeekSchedule.FromWeekdays(trained.Select(d => d.Weekday!.Value)), trained);
    }
}
