using System;
using GymApp.Core;

namespace GymApp.Data.Models;

public class CalorieLog
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public DateOnly Date { get; set; }
    public int CaloriesConsumed { get; set; }
    public int? GoalAtTime { get; set; }              // snapshot, since the goal can change later
    public GoalType? GoalTypeAtTime { get; set; }     // snapshot too, of what the user was aiming for that day
    /// <summary>True when the day was logged as a typed total before food entries started (Phase 26): deleting its last
    /// entry then leaves a log of 0 instead of removing the day's log.</summary>
    public bool LoggedAsTotal { get; set; }
}