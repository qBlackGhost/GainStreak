namespace GymApp.Data.Models;

/// <summary>A user's exercise list for one split day. Per user, because preset days are shared by everyone.</summary>
public class UserDayExercise
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int SplitDayId { get; set; }
    public SplitDay SplitDay { get; set; } = null!;
    public int ExerciseId { get; set; }
    public Exercise Exercise { get; set; } = null!;
    public int Order { get; set; }
    public int WarmupSets { get; set; }
    public int WorkingSets { get; set; } = 3;
}