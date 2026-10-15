using System;
using GymApp.Core;

namespace GymApp.Data.Models;

/// <summary>One line of the points ledger. DedupKey (e.g. "workout:2026-10-05", "week:2026-10-05") is unique per
/// user, so a day or week can never be paid twice. User.Points is the cached sum of these.</summary>
public class PointsEntry
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public PointsReason Reason { get; set; }
    public int Amount { get; set; }
    public DateOnly Date { get; set; }
    public string DedupKey { get; set; } = "";
}
