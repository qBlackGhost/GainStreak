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
    public ActivityLevel? ActivityLevel { get; set; }  // picked from three step bands
    public UnitSystem Units { get; set; } = UnitSystem.Metric;

    public int? HomeCityId { get; set; }               // default city for the gym picker; other cities can still be searched
    public City? HomeCity { get; set; }
    public bool AskForGymAfterCheckIn { get; set; } = true;   // "Don't ask me again" on the check-in prompt sets this to false

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
