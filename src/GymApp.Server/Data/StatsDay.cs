namespace GymApp.Server.Data;

/// <summary>One day of a user's uploaded projection: what their stats will be on <see cref="Date"/> if nothing new is
/// logged. Friends see the row for their own today. The phone computes these; the server only stores them.</summary>
public class StatsDay
{
    public int UserId { get; set; }
    public DateOnly Date { get; set; }
    public int TotalPoints { get; set; }
    public int WeekPoints { get; set; }
    public int CurrentStreak { get; set; }
}
