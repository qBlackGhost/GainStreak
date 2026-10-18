using System;
using GymApp.Core;

namespace GymApp.Data.Models;

/// <summary>The user's choice about one missed training day. <see cref="Date"/> is the missed day, not the day the
/// choice was made. One row per user per day; misses without a row behave like <see cref="MissChoice.ShiftToRest"/>.</summary>
public class MissedDayChoice
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public DateOnly Date { get; set; }
    public MissChoice Choice { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
