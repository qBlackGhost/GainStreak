using System;

namespace GymApp.Data.Models;

public class CalorieLog
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public DateOnly Date { get; set; }
    public int CaloriesConsumed { get; set; }
    public int? GoalAtTime { get; set; }              // snapshot, since the goal can change later
}