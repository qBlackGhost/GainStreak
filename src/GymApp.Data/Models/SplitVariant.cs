namespace GymApp.Data.Models;

/// <summary>A weekly layout of a preset split, e.g. Full Body "2 days / week".</summary>
public class SplitVariant
{
    public int Id { get; set; }
    public int SplitId { get; set; }
    public WorkoutSplit Split { get; set; } = null!;
    public string Name { get; set; } = "";       // "3 days / week"
    public string Pattern { get; set; } = "";    // 7 chars, W = workout, R = rest; day 1 is the day the user picks
    public bool IsDefault { get; set; }
}
