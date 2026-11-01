using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Client;
using GymApp.Contracts;
using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Social;
using CorePeriod = GymApp.Core.LeaderboardPeriod;
using Relation = GymApp.Social.FriendRelation;

namespace GymApp.Services;

/// <summary>Shared by the remote social services: they act as the signed-in user only.</summary>
internal static class SignedInGuard
{
    /// <summary>The <c>userId</c> a view model passes must be the signed-in local user. Anything else is a bug (the
    /// server can only answer for the token's owner), so it throws instead of quietly answering for someone else.</summary>
    public static void Check(SessionService session, int userId)
    {
        if (session.CurrentUser?.Id != userId)
            throw new InvalidOperationException($"The remote social services act as the signed-in user only (asked for local user {userId}).");
    }
}

/// <summary>
/// Friends on the social server, which runs the rules. Writes need a connection (they aren't queued): offline they
/// return the offline message and change nothing. Reads are cached per user and come from the cache, marked stale,
/// when the server can't be reached. Ids in the results, and <c>otherId</c> arguments, are server ids.
/// </summary>
public class RemoteFriendService(SocialApiClient api, SocialCacheStore cache, SessionService session) : IFriendService
{
    public async Task<FriendSearchResult> SearchAsync(int userId, string query)
    {
        SignedInGuard.Check(session, userId);
        query = query.Trim();
        // Checked here too, so a short query costs no request (and works offline)
        if (query.Length < FriendService.MinSearchLength)
            return new(false, [], $"Type at least {FriendService.MinSearchLength} characters.");

        var r = await api.SearchAsync(query);
        if (!r.IsOk) return new(false, [], r.Error);
        return new(true, r.Value!.Select(u => new FoundUser(u.UserId, u.Username, ToSocial(u.Relation))).ToList());
    }

    public async Task<FriendResult> SendRequestAsync(int userId, int otherId)
    {
        SignedInGuard.Check(session, userId);
        var r = await api.SendRequestAsync(otherId);
        return r.IsOk ? new FriendResult(true, BecameFriends: r.Value!.BecameFriends) : Refused(r);
    }

    public Task<FriendResult> AcceptAsync(int userId, int otherId) => WriteAsync(userId, () => api.AcceptAsync(otherId));
    public Task<FriendResult> DeclineAsync(int userId, int otherId) => WriteAsync(userId, () => api.DeclineAsync(otherId));
    public Task<FriendResult> CancelAsync(int userId, int otherId) => WriteAsync(userId, () => api.CancelRequestAsync(otherId));
    public Task<FriendResult> RemoveAsync(int userId, int otherId) => WriteAsync(userId, () => api.RemoveFriendAsync(otherId));

    public async Task<IReadOnlyList<Friend>> GetFriendsAsync(int userId)
    {
        SignedInGuard.Check(session, userId);
        var r = await api.GetFriendsAsync();
        if (r.IsOk)
        {
            await cache.SaveAsync(userId, SocialCacheKeys.Friends, r.Value!);
            return new FriendList(ToFriends(r.Value!), fetchedAt: cache.Now);
        }
        return await cache.GetAsync<List<FriendResponse>>(userId, SocialCacheKeys.Friends) is { } hit
            ? new FriendList(ToFriends(hit.Value), isStale: true, fetchedAt: hit.FetchedAt)
            : new FriendList([], isStale: true);
    }

    public async Task<FriendRequests> GetRequestsAsync(int userId)
    {
        SignedInGuard.Check(session, userId);
        var r = await api.GetRequestsAsync();
        if (r.IsOk)
        {
            await cache.SaveAsync(userId, SocialCacheKeys.Requests, r.Value!);
            return ToRequests(r.Value!) with { FetchedAt = cache.Now };
        }
        return await cache.GetAsync<FriendRequestsResponse>(userId, SocialCacheKeys.Requests) is { } hit
            ? ToRequests(hit.Value) with { IsStale = true, FetchedAt = hit.FetchedAt }
            : new FriendRequests([], []) { IsStale = true };
    }

    /// <summary>Offline, the count from the cached requests (0 with none).</summary>
    public async Task<int> IncomingCountAsync(int userId)
    {
        SignedInGuard.Check(session, userId);
        var r = await api.GetIncomingCountAsync();
        if (r.IsOk) return r.Value!.Count;
        return (await cache.GetAsync<FriendRequestsResponse>(userId, SocialCacheKeys.Requests))?.Value.Incoming.Count ?? 0;
    }

    public Task<FriendResult> BlockAsync(int userId, int otherId) => WriteAsync(userId, () => api.BlockAsync(otherId));
    public Task<FriendResult> UnblockAsync(int userId, int otherId) => WriteAsync(userId, () => api.UnblockAsync(otherId));

    /// <summary>Not cached: offline the list is empty, and unblocking needs a connection anyway.</summary>
    public async Task<IReadOnlyList<BlockedUser>> GetBlockedAsync(int userId)
    {
        SignedInGuard.Check(session, userId);
        var r = await api.GetBlockedAsync();
        return r.IsOk ? r.Value!.Select(b => new BlockedUser(b.UserId, b.Username, b.BlockedAt)).ToList() : [];
    }

    private async Task<FriendResult> WriteAsync(int userId, Func<Task<ApiResult>> call)
    {
        SignedInGuard.Check(session, userId);
        var r = await call();
        return r.IsOk ? new FriendResult(true) : Refused(r);
    }

    /// <summary>The server's message, or offline's. Any 409 is another write getting there first: the existing message.</summary>
    private static FriendResult Refused(ApiResult r) =>
        new(false, r.Status == 409 ? FriendService.ConflictError : r.Error);

    private static List<Friend> ToFriends(IEnumerable<FriendResponse> friends) =>
        friends.Select(f => new Friend(f.UserId, f.Username, f.Since)).ToList();

    private static FriendRequests ToRequests(FriendRequestsResponse r)
    {
        static List<FriendRequest> Map(IEnumerable<FriendRequestResponse> x) =>
            x.Select(q => new FriendRequest(q.UserId, q.Username, q.SentAt)).ToList();
        return new FriendRequests(Map(r.Incoming), Map(r.Outgoing));
    }

    private static Relation ToSocial(GymApp.Contracts.FriendRelation relation) => relation switch
    {
        GymApp.Contracts.FriendRelation.Friend => Relation.Friend,
        GymApp.Contracts.FriendRelation.RequestSent => Relation.RequestSent,
        GymApp.Contracts.FriendRelation.RequestReceived => Relation.RequestReceived,
        _ => Relation.None
    };
}

/// <summary>The friends-only leaderboard from the server, which ranks the uploaded projections (14f) by the phone's own
/// date. Cached per period like the friend lists.</summary>
public class RemoteLeaderboardService(SocialApiClient api, SocialCacheStore cache, SessionService session) : ILeaderboardService
{
    public async Task<LeaderboardView?> GetAsync(int userId, CorePeriod period, DateOnly? today = null)
    {
        SignedInGuard.Check(session, userId);
        var day = today ?? AppClock.Today;
        var key = period == CorePeriod.ThisWeek ? SocialCacheKeys.LeaderboardThisWeek : SocialCacheKeys.LeaderboardAllTime;

        var r = await api.GetLeaderboardAsync(
            period == CorePeriod.ThisWeek ? GymApp.Contracts.LeaderboardPeriod.ThisWeek : GymApp.Contracts.LeaderboardPeriod.AllTime, day);
        if (r.IsOk)
        {
            await cache.SaveAsync(userId, key, r.Value!);
            return ToView(period, r.Value!) with { FetchedAt = cache.Now };
        }
        if (await cache.GetAsync<LeaderboardResponse>(userId, key) is { } hit)
            return ToView(period, hit.Value) with { IsStale = true, FetchedAt = hit.FetchedAt };

        // Nothing cached yet: an empty board that says so, rather than "add friends"
        var (monday, sunday) = Leaderboard.WeekRange(day);
        return new LeaderboardView(period, monday, sunday, []) { IsStale = true };
    }

    private static LeaderboardView ToView(CorePeriod period, LeaderboardResponse r) =>
        new(period, r.WeekStart, r.WeekEnd, r.Rows
            .Select(x => new LeaderboardRow(x.Rank, x.UserId, x.Username, x.Points, LevelCalculator.ForNumber(x.Level), x.CurrentStreak, x.IsYou, x.Avatar))
            .ToList());
}
