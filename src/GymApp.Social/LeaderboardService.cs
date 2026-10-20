using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Core;

namespace GymApp.Social;

/// <summary>The board for one period. <see cref="WeekStart"/>..<see cref="WeekEnd"/> is the week containing today,
/// also on the all-time board. <see cref="IsAlone"/>: only you on it, so the UI shows "Add friends to compete".</summary>
public record LeaderboardView(LeaderboardPeriod Period, DateOnly WeekStart, DateOnly WeekEnd, IReadOnlyList<LeaderboardRow> Rows)
{
    public bool IsAlone => Rows.Count <= 1;
    public LeaderboardRow? You => Rows.FirstOrDefault(r => r.IsYou);
}

/// <summary>The friends-only leaderboard: you and your accepted friends, never pending requests or other users.
/// Only reads points and streaks; ranking is <see cref="Leaderboard"/> in Core.</summary>
public class LeaderboardService(ISocialStore store)
{
    /// <summary>The board, or null if the user doesn't exist.</summary>
    public async Task<LeaderboardView?> GetAsync(int userId, LeaderboardPeriod period, DateOnly? today = null)
    {
        var day = today ?? AppClock.Today;
        var (monday, sunday) = Leaderboard.WeekRange(day);

        List<int> ids;
        await using (var s = await store.OpenAsync())
        {
            if (await s.FindUserAsync(userId) is null) return null;
            ids = (await s.GetFriendshipsAsync(userId))
                .Where(l => l.Friendship.Status == FriendshipStatus.Accepted)
                .Select(l => l.Other.Id)
                .Prepend(userId)
                .ToList();
        }

        // Outside the session: the refresh writes through its own contexts
        await store.RefreshStatsAsync(ids, day);

        IReadOnlyList<PublicStats> stats;
        await using (var s = await store.OpenAsync())
            stats = await s.GetPublicStatsAsync(ids, monday, sunday, day);
        if (stats.All(x => x.UserId != userId)) return null;   // deleted meanwhile

        var entries = stats.Select(x => new LeaderboardEntry(x.UserId, x.Username,
            period == LeaderboardPeriod.ThisWeek ? x.WeekPoints : x.TotalPoints, x.TotalPoints, x.CurrentStreak));
        return new LeaderboardView(period, monday, sunday, Leaderboard.Rank(entries, userId));
    }
}
