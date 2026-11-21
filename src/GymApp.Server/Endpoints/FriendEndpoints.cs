using System.Security.Claims;
using GymApp.Contracts;
using GymApp.Server.Auth;
using GymApp.Server.Data;
using GymApp.Server.Friends;
using GymApp.Server.Push;
using GymApp.Social;
using Microsoft.EntityFrameworkCore;
using Relation = GymApp.Contracts.FriendRelation;

namespace GymApp.Server.Endpoints;

/// <summary><c>/v1/users/search</c>, <c>/v1/friends</c>, <c>/v1/invites</c> and <c>/v1/blocks</c>: thin wrappers over the shared <see cref="FriendService"/>,
/// always acting as the signed-in user, so the app's rules (limits, only the receiver accepts) hold for any client.
/// A request, an accept and an accepted invite notify the other user's phones (Phase 22) once the rule succeeded; the
/// answer is the same whether or not that push goes through.</summary>
public static class FriendEndpoints
{
    public static void MapFriends(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/users/search", Search).RequireRateLimiting(RateLimits.Search);

        var friends = app.MapGroup("/v1/friends");
        friends.MapGet("", GetFriends);
        friends.MapGet("/requests", GetRequests);
        friends.MapGet("/requests/count", GetIncomingCount);
        friends.MapPost("/{userId:int}/request", SendRequest);
        friends.MapPost("/{userId:int}/accept", Accept);
        friends.MapPost("/{userId:int}/decline", (int userId, ClaimsPrincipal me, FriendService friends) =>
            Run(friends.DeclineAsync(me.UserId(), userId)));
        friends.MapDelete("/{userId:int}/request", (int userId, ClaimsPrincipal me, FriendService friends) =>
            Run(friends.CancelAsync(me.UserId(), userId)));
        friends.MapDelete("/{userId:int}", (int userId, ClaimsPrincipal me, FriendService friends) =>
            Run(friends.RemoveAsync(me.UserId(), userId)));

        // Invites (Phase 21): your own code, and someone else's from a scanned QR code or link
        friends.MapGet("/invite", GetInvite);
        friends.MapPost("/invite/reset", ResetInvite).RequireRateLimiting(RateLimits.InviteReset);
        var invites = app.MapGroup("/v1/invites").RequireRateLimiting(RateLimits.Invite);
        invites.MapGet("/{code}", LookUpInvite);
        invites.MapPost("/{code}/accept", AcceptInvite);

        var blocks = app.MapGroup("/v1/blocks");
        blocks.MapGet("", GetBlocked);
        blocks.MapPut("/{userId:int}", Block);
        blocks.MapDelete("/{userId:int}", (int userId, ClaimsPrincipal me, FriendService friends) =>
            Run(friends.UnblockAsync(me.UserId(), userId)));
    }

    private static async Task<IResult> GetBlocked(ClaimsPrincipal me, FriendService friends) =>
        Results.Ok((await friends.GetBlockedAsync(me.UserId()))
            .Select(b => new BlockedUserResponse(b.UserId, b.Username, b.BlockedAt)).ToList());

    private static async Task<IResult> Block(int userId, ClaimsPrincipal me, FriendService friends, ServerDbContext db)
    {
        var id = me.UserId();
        var result = await friends.BlockAsync(id, userId);
        if (!result.Success) return Refused(result);
        // Already hidden, since only friends' shares are listed; gone, so unblocking doesn't bring them back
        await db.Shares.Where(s => (s.FromUserId == id && s.ToUserId == userId) || (s.FromUserId == userId && s.ToUserId == id))
            .ExecuteDeleteAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> Search(string? q, ClaimsPrincipal me, FriendService friends)
    {
        var result = await friends.SearchAsync(me.UserId(), q ?? "");
        if (!result.Success) return Problems.BadRequest(result.Error!);
        return Results.Ok(result.Users.Select(u => new FoundUserResponse(u.UserId, u.Username, ToContract(u.Relation))).ToList());
    }

    private static Relation ToContract(GymApp.Social.FriendRelation relation) => relation switch
    {
        GymApp.Social.FriendRelation.Friend => Relation.Friend,
        GymApp.Social.FriendRelation.RequestSent => Relation.RequestSent,
        GymApp.Social.FriendRelation.RequestReceived => Relation.RequestReceived,
        _ => Relation.None
    };

    private static async Task<IResult> GetFriends(ClaimsPrincipal me, FriendService friends) =>
        Results.Ok((await friends.GetFriendsAsync(me.UserId()))
            .Select(f => new FriendResponse(f.UserId, f.Username, f.Since)).ToList());

    private static async Task<IResult> GetRequests(ClaimsPrincipal me, FriendService friends)
    {
        var requests = await friends.GetRequestsAsync(me.UserId());
        static List<FriendRequestResponse> Map(IEnumerable<FriendRequest> r) =>
            r.Select(x => new FriendRequestResponse(x.UserId, x.Username, x.SentAt)).ToList();
        return Results.Ok(new FriendRequestsResponse(Map(requests.Incoming), Map(requests.Outgoing)));
    }

    private static async Task<IResult> GetIncomingCount(ClaimsPrincipal me, FriendService friends) =>
        Results.Ok(new CountResponse(await friends.IncomingCountAsync(me.UserId())));

    private static async Task<IResult> SendRequest(int userId, ClaimsPrincipal me, FriendService friends, FriendNotifier push)
    {
        var result = await friends.SendRequestAsync(me.UserId(), userId);
        if (!result.Success) return Refused(result);
        // Sending accepts a request they had already sent: then it's their request that was accepted
        await push.NotifyAsync(userId, result.BecameFriends ? PushKind.FriendAccepted : PushKind.FriendRequest, me.UserId());
        return Results.Ok(new SendRequestResponse(result.BecameFriends));
    }

    private static async Task<IResult> Accept(int userId, ClaimsPrincipal me, FriendService friends, FriendNotifier push)
    {
        var result = await friends.AcceptAsync(me.UserId(), userId);
        if (!result.Success) return Refused(result);
        await push.NotifyAsync(userId, PushKind.FriendAccepted, me.UserId());
        return Results.NoContent();
    }

    private static async Task<IResult> GetInvite(HttpRequest request, ClaimsPrincipal me, FriendService friends, InviteSettings settings) =>
        Invite(await friends.GetInviteCodeAsync(me.UserId()), request, settings);

    private static async Task<IResult> ResetInvite(HttpRequest request, ClaimsPrincipal me, FriendService friends, InviteSettings settings) =>
        Invite(await friends.ResetInviteCodeAsync(me.UserId()), request, settings);

    private static IResult Invite(InviteCodeResult result, HttpRequest request, InviteSettings settings) =>
        result.Success
            ? Results.Ok(new FriendInviteResponse(result.Code!, settings.LinkFor(result.Code!, request)))
            : Refused(new FriendResult(false, result.Error));

    private static async Task<IResult> LookUpInvite(string code, ClaimsPrincipal me, FriendService friends)
    {
        var result = await friends.LookUpInviteAsync(me.UserId(), code);
        return result.Success
            ? Results.Ok(new InviteLookupResponse(result.Inviter!.Id, result.Inviter.Username, ToContract(result.Relation)))
            : Refused(new FriendResult(false, result.Error));
    }

    private static async Task<IResult> AcceptInvite(string code, ClaimsPrincipal me, FriendService friends, FriendNotifier push,
        ServerDbContext db, TimeProvider clock)
    {
        var result = await friends.AcceptInviteAsync(me.UserId(), code);
        if (!result.Success) return Refused(result);
        if (result.OtherUserId is { } inviter)
        {
            await RecordInviteAsync(db, inviter, me.UserId(), clock);
            await push.NotifyAsync(inviter, PushKind.InviteAccepted, me.UserId());
        }
        return Results.Ok(new SendRequestResponse(result.BecameFriends));
    }

    /// <summary>The Recruiter badge (Phase 25): who joined through whose invite. Once per pair, even if they unfriend and
    /// come back through the invite again.</summary>
    private static async Task RecordInviteAsync(ServerDbContext db, int inviter, int invitee, TimeProvider clock)
    {
        if (await db.InviteAccepts.AnyAsync(x => x.InviterId == inviter && x.InviteeId == invitee)) return;
        db.InviteAccepts.Add(new InviteAccept { InviterId = inviter, InviteeId = invitee, AcceptedAt = clock.GetUtcNow().UtcDateTime });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { /* the same pair at the same moment: already recorded */ }
    }

    private static async Task<IResult> Run(Task<FriendResult> action)
    {
        var result = await action;
        return result.Success ? Results.NoContent() : Refused(result);
    }

    private static IResult Refused(FriendResult result) => result.Error switch
    {
        FriendService.UnknownUserError or FriendService.UnknownInviteError => Problems.NotFound(result.Error),
        FriendService.ConflictError => Problems.Conflict(result.Error),
        _ => Problems.BadRequest(result.Error!)
    };
}
