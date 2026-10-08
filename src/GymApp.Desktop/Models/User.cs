using System;
using System.Collections.Generic;

namespace GymApp.Desktop.Models;

public enum GoalType { Lose, Maintain, Gain }

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";   // never the plain password
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int? SelectedSplitId { get; set; }
    public WorkoutSplit? SelectedSplit { get; set; }
    public string? SelectedCity { get; set; }

    public int Points { get; set; }                   // level is derived from this
    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }

    public int? DailyCalorieGoal { get; set; }
    public GoalType GoalType { get; set; } = GoalType.Maintain;

    public List<WorkoutLog> WorkoutLogs { get; set; } = new();
    public List<CalorieLog> CalorieLogs { get; set; } = new();
}
