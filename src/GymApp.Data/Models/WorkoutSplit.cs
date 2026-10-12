using System.Collections.Generic;

namespace GymApp.Data.Models;

public class WorkoutSplit
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsRecommended { get; set; }
    public int? OwnerUserId { get; set; }             // null = built-in preset
    public User? Owner { get; set; }
    public List<SplitDay> Days { get; set; } = new();
    public List<SplitVariant> Variants { get; set; } = new();
}