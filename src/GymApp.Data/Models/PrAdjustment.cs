namespace GymApp.Data.Models;

/// <summary>A manual PR correction (see PrCalculator). One per user, exercise and day.</summary>
public class PrAdjustment
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int ExerciseId { get; set; }
    public Exercise Exercise { get; set; } = null!;
    public DateOnly Date { get; set; }
    public double WeightKg { get; set; }
    public int Reps { get; set; }
    public double? OneRepMaxKg { get; set; }         // null = estimate with Epley
}