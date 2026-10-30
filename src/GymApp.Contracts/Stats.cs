namespace GymApp.Contracts;

/// <summary>One projected day: what the stats will be on <see cref="Date"/> if nothing new is logged.</summary>
public sealed record StatsDayRequest(DateOnly Date, int TotalPoints, int WeekPoints, int CurrentStreak);

/// <summary>Replaces every day the server holds for you. At most 31 days, no date twice, the first within 2 days of today.
/// <see cref="Split"/> also replaces your active split, for the split usage counts; null means none chosen.</summary>
public sealed record StatsUploadRequest(IReadOnlyList<StatsDayRequest> Days, ActiveSplitRequest? Split = null);

/// <summary>Sent as <c>thisWeek</c> or <c>allTime</c>, also in the <c>period</c> query parameter.</summary>
public enum LeaderboardPeriod { ThisWeek, AllTime }

/// <summary><see cref="Points"/> is what the row is ranked by (the period's points); the level is always the all-time one.
/// <see cref="Avatar"/> is the user's choice in <c>AvatarChoice</c> form, null for the plain initials.</summary>
public sealed record LeaderboardRowResponse(
    int Rank, int UserId, string Username, int Points, int Level, string LevelName, int CurrentStreak, bool IsYou, string? Avatar = null);

/// <summary>The board for one period. <see cref="WeekStart"/>..<see cref="WeekEnd"/> is the week containing the viewer's today.</summary>
public sealed record LeaderboardResponse(
    LeaderboardPeriod Period, DateOnly WeekStart, DateOnly WeekEnd, IReadOnlyList<LeaderboardRowResponse> Rows);
