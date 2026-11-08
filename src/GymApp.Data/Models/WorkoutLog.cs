using System;

namespace GymApp.Data.Models;

public class WorkoutLog
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public DateOnly Date { get; set; }
    public int? SplitDayId { get; set; }
    public SplitDay? SplitDay { get; set; }
    /// <summary>A rest day's workout of hand-picked exercises (0.18.1): their ids separated by commas, so the log's
    /// completion can follow later set changes. Null for a split day's workout (<see cref="SplitDayId"/>).</summary>
    public string? ExerciseIds { get; set; }
    public int? GymId { get; set; }
    public Gym? Gym { get; set; }
    /// <summary>Optional cardio done with the workout, in minutes. Null when none was entered.</summary>
    public int? CardioMinutes { get; set; }
    /// <summary>Done near one of the user's gyms (Phase 18). Null for workouts logged before 0.18.0.</summary>
    public bool? Verified { get; set; }
    /// <summary>The day's exercises filled and planned, kept up to date while today's sets change. Null for workouts
    /// logged before 0.18.0, which keep their full points.</summary>
    public int? FilledExercises { get; set; }
    public int? TotalExercises { get; set; }
}