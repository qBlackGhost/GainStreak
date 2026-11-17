using System.Security.Claims;
using GymApp.Contracts;
using GymApp.Server.Auth;
using GymApp.Server.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GymApp.Server.Endpoints;

/// <summary><c>/v1/me/push-device</c> (Phase 22): the calling session's push token. Each session has at most one, and
/// it's deleted with the session (logout, a password change or reset, account deletion).</summary>
public static class PushEndpoints
{
    public const string ConflictMessage = "That push token just changed. Try again.";

    public static void MapPush(this IEndpointRouteBuilder app)
    {
        app.MapPut("/v1/me/push-device", Register).RequireRateLimiting(RateLimits.PushDevice);
        app.MapDelete("/v1/me/push-device", Unregister);
    }

    /// <summary>Registers or replaces this session's token. The same token on another session (another account on the
    /// same phone, or this phone's previous sign-in) moves here.</summary>
    private static async Task<IResult> Register(PushDeviceRequest request, ClaimsPrincipal me, ServerDbContext db, TimeProvider clock)
    {
        if (!PushRules.IsValidToken(request.Token) || !Enum.IsDefined(request.Platform))
            return Problems.BadRequest(PushRules.BadTokenMessage);

        var userId = me.UserId();
        var sessionId = me.SessionId();
        var now = clock.GetUtcNow().UtcDateTime;
        var token = request.Token;
        var platform = request.Platform;
        var sandbox = platform == PushPlatform.Apns && request.Sandbox;

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.PushDevices.Where(d => d.Token == token && d.SessionId != sessionId).ExecuteDeleteAsync();
            // While here: this user's tokens on sessions that expired (no background job on a host that scales to zero)
            await db.PushDevices
                .Where(d => d.UserId == userId && db.Sessions.Any(s => s.Id == d.SessionId && s.ExpiresAt <= now))
                .ExecuteDeleteAsync();
            var updated = await db.PushDevices.Where(d => d.SessionId == sessionId).ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Platform, platform)
                .SetProperty(d => d.Token, token)
                .SetProperty(d => d.Sandbox, sandbox)
                .SetProperty(d => d.UpdatedAt, now));
            if (updated == 0)
            {
                db.PushDevices.Add(new PushDevice
                {
                    SessionId = sessionId, UserId = userId, Platform = platform, Token = token, Sandbox = sandbox,
                    CreatedAt = now, UpdatedAt = now
                });
                await db.SaveChangesAsync();
            }
            await tx.CommitAsync();
        }
        catch (Exception e) when (e is DbUpdateException or PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The same token or session registered at the same moment by another request
            return Problems.Conflict(ConflictMessage);
        }
        return Results.NoContent();
    }

    private static async Task<IResult> Unregister(ClaimsPrincipal me, ServerDbContext db)
    {
        var sessionId = me.SessionId();
        await db.PushDevices.Where(d => d.SessionId == sessionId).ExecuteDeleteAsync();
        return Results.NoContent();
    }
}
