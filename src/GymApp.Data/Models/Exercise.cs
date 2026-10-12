namespace GymApp.Data.Models;

public enum MuscleGroup { Chest, Back, Shoulders, Biceps, Triceps, Forearms, Quads, Hamstrings, Glutes, Calves, Core }

public class Exercise
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public MuscleGroup MuscleGroup { get; set; }
    public int? OwnerUserId { get; set; }            // null = built-in
    public User? Owner { get; set; }
}