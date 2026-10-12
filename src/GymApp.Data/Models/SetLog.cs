using GymApp.Core;

namespace GymApp.Data.Models;

/// <summary>One logged set. Attached to user + date, not to WorkoutLog, so it never affects the streak.</summary>
public class SetLog
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int ExerciseId { get; set; }
    public Exercise Exercise { get; set; } = null!;
    public DateOnly Date { get; set; }
    public int SetNumber { get; set; }
    public SetKind Kind { get; set; }
    public int Reps { get; set; }
    public double WeightKg { get; set; }
}