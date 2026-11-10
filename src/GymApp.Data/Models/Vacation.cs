using System;

namespace GymApp.Data.Models;

/// <summary>A vacation (0.19.0): the streak is frozen from <see cref="StartDate"/> up to the day before
/// <see cref="ReturnDate"/>, or up to <see cref="EndedEarlyOn"/> when the user came back early. See
/// <see cref="Core.VacationRules"/>. Kept after it ends: the frozen days and the 60-day cooldown read the old rows.</summary>
public class Vacation
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public DateOnly StartDate { get; set; }
    public DateOnly ReturnDate { get; set; }       // the first normal day again
    public DateOnly? EndedEarlyOn { get; set; }    // "I'm back": the last frozen day
    public bool WelcomeShown { get; set; }         // Home showed "Welcome back!" on the first normal day
    public int? VisitingCityId { get; set; }       // "Show me gyms near me" (19d): the map shows it until the vacation ends
    public City? VisitingCity { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
