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
    public int? GymId { get; set; }
    public Gym? Gym { get; set; }
}