using System.Security.Claims;
using GymApp.Contracts;
using GymApp.Server.Auth;
using GymApp.Server.Data;
using GymApp.Social;
using Microsoft.EntityFrameworkCore;
using CorePeriod = GymApp.Core.LeaderboardPeriod;
using Period = GymApp.Contracts.LeaderboardPeriod;

namespace GymApp.Server.Endpoints;

/// <summary><c>PUT /v1/me/stats</c> (the phone's projection and active split), <c>GET /v1/leaderboard</c> (the shared
/// <see cref="LeaderboardService"/> over the stored projections) and <c>GET /v1/splits/usage</c> (how many users
/// train with each split, over every account).</summary>
public static class StatsEndpoints
{
    public const int MaxDays = 31;

    /// <summary>How long days before the newest projection are kept: enough for last week's board.</summary>
    public const int HistoryDays = 21;

    public static void MapStats(this IEndpointRouteBuilder app)
    {
        app.MapPut("/v1/me/stats", UploadStats);
        app.MapGet("/v1/leaderboard", GetLeaderboard);
        app.MapGet("/v1/splits/usage", GetSplitUsage);
    }

    private static async Task<IResult> UploadStats(StatsUploadRequest request, ClaimsPrincipal me, ServerDbContext db, TimeProvider clock)
    {
        var days = request.Days ?? [];
        if (days.Count == 0) return Problems.BadRequest("Send at least one day.");
        if (days.Count > MaxDays) return Problems.BadRequest($"Send at most {MaxDays} days.");
        if (days.DistinctBy(d => d.Date).Count() != days.Count) return Problems.BadRequest("Each date can only be sent once.");
        if (!clock.IsNearServerDate(days.Min(d => d.Date)))
            return Problems.BadRequest($"The first day must be within {ClientDates.AllowanceDays} days of today.");
        if (days.Any(d => d.TotalPoints < 0 || d.WeekPoints < 0 || d.CurrentStreak < 0))
            return Problems.BadRequest("Points and streaks can't be negative.");
        if (request.Split?.PresetId <= 0) return Problems.BadRequest("Unknown preset split.");

        var userId = me.UserId();
        // The days before the first one sent stay (Phase 25): last week's board reads each user's last Sunday, which a
        // new projection starting this week no longer holds. Older than HistoryDays, they go.
        var first = days.Min(d => d.Date);
        var oldest = clock.ServerToday().AddDays(-HistoryDays);
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.ProjectedStats.Where(d => d.UserId == userId && (d.Date >= first || d.Date < oldest)).ExecuteDeleteAsync();
        db.ProjectedStats.AddRange(days.Select(d => new StatsDay
        {
            UserId = userId, Date = d.Date, TotalPoints = d.TotalPoints, WeekPoints = d.WeekPoints, CurrentStreak = d.CurrentStreak
        }));
        await db.SaveChangesAsync();
        var presetId = request.Split?.PresetId;
        var custom = request.Split is { PresetId: null };
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(u => u
            .SetProperty(x => x.SplitPresetId, presetId)
            .SetProperty(x => x.UsesCustomSplit, custom));
        await tx.CommitAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> GetSplitUsage(ServerDbContext db)
    {
        var presets = await db.Users.Where(u => u.SplitPresetId != null)
            .GroupBy(u => u.SplitPresetId!.Value)
            .Select(g => new SplitUsageRow(g.Key, g.Count()))
            .ToListAsync();
        var custom = await db.Users.CountAsync(u => u.UsesCustomSplit);
        return Results.Ok(new SplitUsageResponse(presets.Sum(p => p.Users) + custom,
            presets.OrderByDescending(p => p.Users).ThenBy(p => p.PresetId).ToList(), custom));
    }

    private static async Task<IResult> GetLeaderboard(string? period, string? today, ClaimsPrincipal me,
        LeaderboardService leaderboard, TimeProvider clock)
    {
        Period? parsed = period?.ToLowerInvariant() switch
        {
            "thisweek" => Period.ThisWeek,
            "alltime" => Period.AllTime,
            "lastweek" => Period.LastWeek,
            _ => null
        };
        if (parsed is null) return Problems.BadRequest("Set period to thisWeek, allTime or lastWeek.");
        if (ClientDates.Parse(today) is not { } day) return Problems.BadRequest($"Send today's date as {ApiFormat.Date}.");
        if (!clock.IsNearServerDate(day)) return Problems.BadRequest($"Today must be within {ClientDates.AllowanceDays} days of the server's date.");

        var view = await leaderboard.GetAsync(me.UserId(), parsed switch
        {
            Period.ThisWeek => CorePeriod.ThisWeek,
            Period.LastWeek => CorePeriod.LastWeek,
            _ => CorePeriod.AllTime,
        }, day);
        if (view is null) return Results.NotFound();

        return Results.Ok(new LeaderboardResponse(parsed.Value, view.WeekStart, view.WeekEnd,
            view.Rows.Select(r => new LeaderboardRowResponse(
                r.Rank, r.UserId, r.Username, r.Points, r.Level.Number, r.Level.Name, r.CurrentStreak, r.IsYou, r.Avatar,
                r.FeaturedBadge)).ToList()));
    }
}
