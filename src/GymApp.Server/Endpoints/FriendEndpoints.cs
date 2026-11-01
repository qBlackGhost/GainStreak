using System.Security.Claims;
using GymApp.Contracts;
using GymApp.Server.Auth;
using GymApp.Server.Data;
using GymApp.Social;
using Microsoft.EntityFrameworkCore;
using Relation = GymApp.Contracts.FriendRelation;

namespace GymApp.Server.Endpoints;

/// <summary><c>/v1/users/search</c>, <c>/v1/friends</c> and <c>/v1/blocks</c>: thin wrappers over the shared <see cref="FriendService"/>,
/// always acting as the signed-in user, so the app's rules (limits, only the receiver accepts) hold for any client.</summary>
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
        friends.MapPost("/{userId:int}/accept", (int userId, ClaimsPrincipal me, FriendService friends) =>
            Run(friends.AcceptAsync(me.UserId(), userId)));
        friends.MapPost("/{userId:int}/decline", (int userId, ClaimsPrincipal me, FriendService friends) =>
            Run(friends.DeclineAsync(me.UserId(), userId)));
        friends.MapDelete("/{userId:int}/request", (int userId, ClaimsPrincipal me, FriendService friends) =>
            Run(friends.CancelAsync(me.UserId(), userId)));
        friends.MapDelete("/{userId:int}", (int userId, ClaimsPrincipal me, FriendService friends) =>
            Run(friends.RemoveAsync(me.UserId(), userId)));

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

    private static async Task<IResult> SendRequest(int userId, ClaimsPrincipal me, FriendService friends)
    {
        var result = await friends.SendRequestAsync(me.UserId(), userId);
        return result.Success ? Results.Ok(new SendRequestResponse(result.BecameFriends)) : Refused(result);
    }

    private static async Task<IResult> Run(Task<FriendResult> action)
    {
        var result = await action;
        return result.Success ? Results.NoContent() : Refused(result);
    }

    private static IResult Refused(FriendResult result) => result.Error switch
    {
        FriendService.UnknownUserError => Problems.NotFound(result.Error),
        FriendService.ConflictError => Problems.Conflict(result.Error),
        _ => Problems.BadRequest(result.Error!)
    };
}
