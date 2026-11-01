using System.Security.Claims;
using System.Text.Json;
using GymApp.Contracts;
using GymApp.Server.Auth;
using GymApp.Server.Data;
using GymApp.Core;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Server.Endpoints;

/// <summary><c>/v1/shares</c>: splits and workouts sent to friends. Only accepted friends can be sent to; the receiver
/// lists theirs and removes each one once added or dismissed (the app applies it on the phone, nothing else happens
/// here). Shares older than <see cref="ShareRules.KeptFor"/> are never listed and are cleaned up on the next send.</summary>
public static class ShareEndpoints
{
    public static void MapShares(this IEndpointRouteBuilder app)
    {
        var shares = app.MapGroup("/v1/shares");
        shares.MapPost("", Send).RequireRateLimiting(RateLimits.Share);
        shares.MapGet("", GetIncoming);
        shares.MapDelete("/{id:long}", Remove);
    }

    private static async Task<IResult> Send(ShareRequest request, ClaimsPrincipal me, ServerDbContext db, TimeProvider clock)
    {
        if (ShareRules.Validate(request) is { } error) return Problems.BadRequest(error);
        var userId = me.UserId();
        var to = request.ToUserIds.Distinct().ToList();

        var friends = await db.Friendships
            .Where(f => f.Status == FriendshipStatus.Accepted && (f.UserAId == userId || f.UserBId == userId))
            .Select(f => f.UserAId == userId ? f.UserBId : f.UserAId)
            .Where(id => to.Contains(id))
            .CountAsync();
        if (friends != to.Count) return Problems.BadRequest(ShareRules.NotFriendsMessage);

        var (kind, payload) = request.Split is { } split
            ? (ShareKind.Split, JsonSerializer.Serialize(split, ApiFormat.Json))
            : (ShareKind.Workout, JsonSerializer.Serialize(request.Workout!, ApiFormat.Json));
        if (payload.Length > Share.MaxPayloadLength) return Problems.BadRequest(ShareRules.InvalidMessage);

        var now = clock.GetUtcNow().UtcDateTime;
        await db.Shares.Where(s => s.CreatedAt < now - ShareRules.KeptFor).ExecuteDeleteAsync();
        foreach (var id in to)
            db.Shares.Add(new Share { FromUserId = userId, ToUserId = id, Kind = kind, Payload = payload, CreatedAt = now });
        await db.SaveChangesAsync();

        // Each receiver keeps their newest shares only
        foreach (var id in to)
        {
            var keep = db.Shares.Where(s => s.ToUserId == id).OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
                .Take(ShareRules.MaxPending).Select(s => s.Id);
            await db.Shares.Where(s => s.ToUserId == id && !keep.Contains(s.Id)).ExecuteDeleteAsync();
        }
        return Results.Ok(new SendShareResponse(to.Count));
    }

    private static async Task<IResult> GetIncoming(ClaimsPrincipal me, ServerDbContext db, TimeProvider clock)
    {
        var userId = me.UserId();
        var since = clock.GetUtcNow().UtcDateTime - ShareRules.KeptFor;
        // Only from people who are still friends: removing a friend hides what they sent
        var friendIds = db.Friendships
            .Where(f => f.Status == FriendshipStatus.Accepted && (f.UserAId == userId || f.UserBId == userId))
            .Select(f => f.UserAId == userId ? f.UserBId : f.UserAId);
        var rows = await (
            from s in db.Shares
            join u in db.Users on s.FromUserId equals u.Id
            where s.ToUserId == userId && s.CreatedAt >= since && friendIds.Contains(s.FromUserId) && u.SuspendedAt == null
            orderby s.CreatedAt descending, s.Id descending
            select new { s.Id, s.FromUserId, u.Username, s.CreatedAt, s.Kind, s.Payload }).ToListAsync();

        return Results.Ok(rows.Select(r => new ShareResponse(r.Id, r.FromUserId, r.Username, r.CreatedAt,
            r.Kind == ShareKind.Split ? JsonSerializer.Deserialize<SharedSplit>(r.Payload, ApiFormat.Json) : null,
            r.Kind == ShareKind.Workout ? JsonSerializer.Deserialize<SharedWorkout>(r.Payload, ApiFormat.Json) : null)).ToList());
    }

    /// <summary>Only the receiver removes a share; someone else's id answers 404 like a missing one.</summary>
    private static async Task<IResult> Remove(long id, ClaimsPrincipal me, ServerDbContext db)
    {
        var deleted = await db.Shares.Where(s => s.Id == id && s.ToUserId == me.UserId()).ExecuteDeleteAsync();
        return deleted == 0 ? Problems.NotFound("That share is gone.") : Results.NoContent();
    }
}
