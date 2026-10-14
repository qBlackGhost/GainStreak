namespace GymApp.Data.Models;

/// <summary>One weigh-in per user per day. The current weight is the latest entry.</summary>
public class WeightLog
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public DateOnly Date { get; set; }
    public double WeightKg { get; set; }
}
