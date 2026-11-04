using System;

namespace GymApp.Data.Models;

/// <summary>A live workout on a phone (Phase 18): one exercise and one set at a time from Home. At most one is open
/// (<see cref="EndedAt"/> null) per user. Kept in the database so a session survives the phone killing the app.</summary>
public class LiveWorkout
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public DateOnly Date { get; set; }
    public int SplitDayId { get; set; }
    public SplitDay SplitDay { get; set; } = null!;
    /// <summary>AppClock time, like the rest of the app's day logic.</summary>
    public DateTime StartedAt { get; set; }
    public DateTime LastActiveAt { get; set; }
    /// <summary>The exercise the user picked from the card's list; null follows the plan's order.</summary>
    public int? CurrentExerciseId { get; set; }
    /// <summary>"Skip for now", in the order skipped: exercise ids separated by commas.</summary>
    public string SkippedExerciseIds { get; set; } = "";
    public DateTime? EndedAt { get; set; }
    /// <summary>The gym the phone was near during the workout (18c), once found; never checked again after that. Only
    /// the result is kept, never a position.</summary>
    public int? VerifiedGymId { get; set; }
    public Gym? VerifiedGym { get; set; }
    /// <summary>Why the session ended by itself, or null when the user ended it: <see cref="FourHours"/> or
    /// <see cref="Midnight"/>. Home shows a one-time note about it, then sets <see cref="StopNoteSeen"/>.</summary>
    public string? AutoStop { get; set; }
    public bool StopNoteSeen { get; set; }

    public const string FourHours = "FourHours", Midnight = "Midnight";
}
