using System;

namespace GymApp.Data.Models;

/// <summary>A cardio timer after a live workout (0.18.2). At most one is open (<see cref="EndedAt"/> null) per user.
/// Kept in the database so the timer survives the phone killing the app; only times are kept, never a position.
/// Finishing saves its whole minutes onto the day's workout log.</summary>
public class CardioSession
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    /// <summary>The workout's day; the minutes go onto that day's log.</summary>
    public DateOnly Date { get; set; }
    /// <summary>The gym the workout was verified at, checked while the timer runs; null when it wasn't verified, and the
    /// timer runs without checks.</summary>
    public int? GymId { get; set; }
    public Gym? Gym { get; set; }
    /// <summary>AppClock time, like the rest of the app's day logic.</summary>
    public DateTime StartedAt { get; set; }
    /// <summary>When the current run started; null while paused.</summary>
    public DateTime? RunningSince { get; set; }
    /// <summary>Seconds counted by earlier runs (before the last pause).</summary>
    public int CountedSeconds { get; set; }
    /// <summary>The last check that found the phone at <see cref="GymId"/>.</summary>
    public DateTime? LastAtGymAt { get; set; }
    /// <summary>Why it's paused: <see cref="ByUser"/> or <see cref="LeftGym"/>; null while running.</summary>
    public string? PauseReason { get; set; }
    public DateTime? EndedAt { get; set; }
    /// <summary>The minutes saved onto the log when it ended; null until then.</summary>
    public int? SavedMinutes { get; set; }

    public const string ByUser = "User", LeftGym = "LeftGym";
}
