using System;
using System.Collections.Generic;
using GymApp.Core;

namespace GymApp.Data.Models;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string? Email { get; set; }               // stored lowercased; null for accounts made before emails existed
    public string PasswordHash { get; set; } = "";   // never the plain password
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? RememberTokenHash { get; set; }
    /// <summary>The linked social-server account, the real login since Phase 14; null for an account not moved online
    /// (and in the TestBench's local mode). Email and PasswordHash are then its cache, for signing in offline.</summary>
    public int? ServerUserId { get; set; }

    public int? SelectedSplitId { get; set; }
    public WorkoutSplit? SelectedSplit { get; set; }
    public int? SelectedVariantId { get; set; }
    public SplitVariant? SelectedVariant { get; set; }
    public DayOfWeek? SplitStartDay { get; set; }   // null = Monday; only used for presets
    public string? SelectedCity { get; set; }

    public int Points { get; set; }                   // cached sum of PointsEntries; level is derived from this
    public bool PointsBackfilled { get; set; }        // the one-time full points rebuild has run
    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }

    public int? DailyCalorieGoal { get; set; }
    public GoalType GoalType { get; set; } = GoalType.Maintain;
    public int? LastSuggestedGoal { get; set; }       // the suggestion the user last accepted or declined, so we only ask again when it changes

    public List<WorkoutLog> WorkoutLogs { get; set; } = new();
    public List<CalorieLog> CalorieLogs { get; set; } = new();
}
