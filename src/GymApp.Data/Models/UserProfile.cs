using GymApp.Core;

namespace GymApp.Data.Models;

/// <summary>Body data for the calorie calculations. One per user; every field can still be empty.
/// Always metric in the database, converted for display based on <see cref="Units"/>.</summary>
public class UserProfile
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public Sex? Sex { get; set; }
    public DateOnly? BirthDate { get; set; }          // the date, not the age, so the age stays right
    public double? HeightCm { get; set; }
    public int? StepsPerDay { get; set; }              // typed in by the user
    public UnitSystem Units { get; set; } = UnitSystem.Metric;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
