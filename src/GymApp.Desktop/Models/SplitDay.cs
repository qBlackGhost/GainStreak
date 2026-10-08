namespace GymApp.Desktop.Models;

public class SplitDay
{
    public int Id { get; set; }
    public int SplitId { get; set; }
    public WorkoutSplit Split { get; set; } = null!;
    public int Order { get; set; }
    public string Name { get; set; } = "";           // "Push", "Legs", ...
}