using System;

namespace GymApp.Data.Models;

/// <summary>A badge the user has earned (Phase 25). One row per user and badge; <see cref="Tier"/> only ever goes up,
/// since earned badges are kept even if the logs behind them change.</summary>
public class EarnedBadge
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string BadgeId { get; set; } = "";           // BadgeCatalog id, e.g. "streak" or "monthly-2026-12"
    public int Tier { get; set; }                       // 1-4 for tiered badges, 0 for one-time ones
    public DateOnly EarnedOn { get; set; }              // when the current tier was reached
    public DateTime? SeenAt { get; set; }               // null until the unlock sheet showed it
}

/// <summary>
/// Something a badge needs that the phone can't work out again later (Phase 25): a day the user trained at the same
/// gym as a friend (<c>we-go-jim</c>; friends' check-ins are only kept 31 days), or a finished leaderboard week
/// (<c>top-of-the-board</c>, <see cref="Value"/> 1 when first). One per user, kind and date.
/// </summary>
public class BadgeEvent
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string Kind { get; set; } = "";
    public DateOnly Date { get; set; }
    public int Value { get; set; }
}
