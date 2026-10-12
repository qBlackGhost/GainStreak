using System;

namespace GymApp.Data.Models;

public class SplitDay
{
    public int Id { get; set; }
    public int SplitId { get; set; }
    public WorkoutSplit Split { get; set; } = null!;
    public int Order { get; set; }
    public string Name { get; set; } = "";           // "Push", "Legs", ...
    public DayOfWeek? Weekday { get; set; }          // null = rotation-based (presets)
}