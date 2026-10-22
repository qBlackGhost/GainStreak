namespace GymApp.Data.Models;

/// <summary>How a user types the weight for a bar exercise: the total, or per side plus the bar. One per user and
/// exercise; no row means total. The sets still store the total.</summary>
public class ExerciseBarSetting
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int ExerciseId { get; set; }
    public Exercise Exercise { get; set; } = null!;
    public bool PerSide { get; set; }
    public double? BarKg { get; set; }               // null = no bar counted
}
