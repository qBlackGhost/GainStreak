using System;

namespace GymApp.Data.Models;

/// <summary>One row per schedule change: from EffectiveFrom on, the user's week looks like WeekPattern.
/// The pattern is a snapshot, so editing a split or its variants later never rewrites past streak days.</summary>
public class UserSchedule
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public DateOnly EffectiveFrom { get; set; }
    public string WeekPattern { get; set; } = "";   // 7 chars, Monday first, W = workout, R = rest
    public int? SplitId { get; set; }                // for reference only
    public WorkoutSplit? Split { get; set; }
}
