using System.Security.Claims;
using GymApp.Contracts;
using GymApp.Core;
using GymApp.Server.Auth;
using GymApp.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Server.Endpoints;

/// <summary>
/// Badges (Phase 25). The phones work them out; the server keeps what each phone uploads so friends can see it:
/// <c>PUT /v1/me/badges</c> (replaces yours, with the featured badge and the sharing setting),
/// <c>GET /v1/friends/{userId}/badges</c> (an accepted friend's, empty when they don't share) and
/// <c>GET /v1/me/invites/accepted</c> (how many people became your friend through your invite, for Recruiter).
/// </summary>
public static class BadgeEndpoints
{
    public const int MaxBadges = 200;

    public static void MapBadges(this IEndpointRouteBuilder app)
    {
        app.MapPut("/v1/me/badges", UploadBadges);
        app.MapGet("/v1/friends/{userId:int}/badges", GetFriendBadges);
        app.MapGet("/v1/me/invites/accepted", GetAcceptedInvites);
    }

    private static async Task<IResult> UploadBadges(BadgesUploadRequest request, ClaimsPrincipal me, ServerDbContext db)
    {
        var badges = request.Badges ?? [];
        if (badges.Count > MaxBadges) return Problems.BadRequest($"Send at most {MaxBadges} badges.");
        if (badges.Any(b => string.IsNullOrWhiteSpace(b.Id) || b.Id.Length > UserBadge.MaxIdLength))
            return Problems.BadRequest($"Badge ids are 1 to {UserBadge.MaxIdLength} characters.");
        if (badges.Any(b => b.Tier is < 0 or > BadgeCatalog.MaxTier)) return Problems.BadRequest($"Tiers are 0 to {BadgeCatalog.MaxTier}.");
        if (badges.DistinctBy(b => b.Id).Count() != badges.Count) return Problems.BadRequest("Each badge can only be sent once.");
        var featured = request.Featured is null ? null : badges.FirstOrDefault(b => b.Id == request.Featured);
        if (request.Featured is not null && featured is null) return Problems.BadRequest("The featured badge must be one of your badges.");

        var userId = me.UserId();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.UserBadges.Where(b => b.UserId == userId).ExecuteDeleteAsync();
        db.UserBadges.AddRange(badges.Select(b => new UserBadge { UserId = userId, BadgeId = b.Id, Tier = b.Tier, EarnedOn = b.EarnedOn }));
        await db.SaveChangesAsync();
        var featuredValue = featured is null ? null : FeaturedBadge.Format(featured.Id, featured.Tier);
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(u => u
            .SetProperty(x => x.FeaturedBadge, featuredValue)
            .SetProperty(x => x.ShareBadges, request.Share));
        await tx.CommitAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> GetFriendBadges(int userId, ClaimsPrincipal me, ServerDbContext db)
    {
        var viewer = me.UserId();
        var (a, b) = viewer < userId ? (viewer, userId) : (userId, viewer);
        var friends = await db.Friendships.AnyAsync(f => f.UserAId == a && f.UserBId == b && f.Status == FriendshipStatus.Accepted);
        var owner = friends
            ? await db.Users.AsNoTracking().Where(u => u.Id == userId && u.SuspendedAt == null)
                .Select(u => new { u.ShareBadges, u.FeaturedBadge }).FirstOrDefaultAsync()
            : null;
        if (owner is null) return Problems.NotFound("Not one of your friends.");
        if (!owner.ShareBadges) return Results.Ok(new UserBadgesResponse(userId, [], null));

        var rows = await db.UserBadges.AsNoTracking().Where(x => x.UserId == userId)
            .OrderByDescending(x => x.EarnedOn).ThenBy(x => x.BadgeId).ToListAsync();
        return Results.Ok(new UserBadgesResponse(userId,
            rows.Select(x => new BadgeDto(x.BadgeId, x.Tier, x.EarnedOn)).ToList(),
            FeaturedBadge.Parse(owner.FeaturedBadge)?.Id));
    }

    private static async Task<IResult> GetAcceptedInvites(ClaimsPrincipal me, ServerDbContext db)
    {
        var userId = me.UserId();
        return Results.Ok(new CountResponse(await db.InviteAccepts.CountAsync(x => x.InviterId == userId)));
    }
}
