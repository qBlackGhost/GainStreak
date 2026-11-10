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

    /// <summary>Share gym check-ins (gym and day, never the time) with accepted friends.
    /// null = not asked yet, false = declined, true = shared. Only true shares anything.</summary>
    public bool? ShareGymCheckIns { get; set; }

    /// <summary>The user's choice in <see cref="Core.AvatarChoice"/> form; null for the plain initials.</summary>
    public string? Avatar { get; set; }

    /// <summary>The user's usual gym, chosen once. Preselected at check-in, or used automatically when the
    /// check-in doesn't ask for a gym.</summary>
    public int? HomeGymId { get; set; }
    public Gym? HomeGym { get; set; }

    /// <summary>"Not on vacation" on the away prompt (0.19.0): not asked again until a fix sees the user back home.</summary>
    public DateTime? VacationPromptDismissedAt { get; set; }

    /// <summary>The last time Home looked for a trip away from the home city (0.19.0), at most every 6 hours.</summary>
    public DateTime? LastAwayCheckAt { get; set; }

    /// <summary>Apple Health on (Phase 20, iPhone): the app writes its workouts, cardio and weigh-ins there and reads
    /// from it. Off until the user turns it on in Profile.</summary>
    public bool HealthEnabled { get; set; }

    /// <summary>When Apple Health was last turned on; the weigh-in import (20c) reaches back 90 days from it.</summary>
    public DateTime? HealthEnabledAt { get; set; }

    /// <summary>The activity level Health's steps last suggested and the user answered (20c), so it's asked again only
    /// when the suggestion changes.</summary>
    public ActivityLevel? HealthStepsSuggestionSeen { get; set; }

    /// <summary>The Health profile values the user answered "Keep mine" to (20c), so the same offer isn't made again.
    /// <see cref="Core.HealthImport.OfferKey"/>.</summary>
    public string? HealthProfileDismissed { get; set; }

    /// <summary>Where the last weigh-in import from Health stopped (an archived HKQueryAnchor, 20c).</summary>
    public byte[]? HealthWeightAnchor { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
