using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Core;

namespace GymApp.Social;

public record FriendResult(bool Success, string? Error = null, bool BecameFriends = false);

/// <summary>How a user relates to you, for the Add / Requested / Accept / Friends button.</summary>
public enum FriendRelation { None, Friend, RequestSent, RequestReceived }

public record FoundUser(int UserId, string Username, FriendRelation Relation);

public record FriendSearchResult(bool Success, IReadOnlyList<FoundUser> Users, string? Error = null);

public record Friend(int UserId, string Username, DateTime Since);

public record FriendRequest(int UserId, string Username, DateTime SentAt);

/// <summary>Pending requests, newest first.</summary>
public record FriendRequests(IReadOnlyList<FriendRequest> Incoming, IReadOnlyList<FriendRequest> Outgoing) : ICachedResult
{
    public bool IsStale { get; init; }
    public DateTime? FetchedAt { get; init; }
}

/// <summary>
/// Friend requests and friendships. Friendship is mutual: a request has to be accepted before either user sees the
/// other on the leaderboard. Declining, cancelling and removing delete the row, so a new request can follow.
/// Social actions never earn points.
/// </summary>
public class FriendService(ISocialStore store, TimeProvider? clock = null) : IFriendService
{
    public const int MaxFriends = 50;
    public const int MaxPendingOutgoing = 20;
    public const int MinSearchLength = 3;
    public const int MaxSearchResults = 20;

    /// <summary>The error when another write got there first. Public so an API can map it to a conflict.</summary>
    public const string ConflictError = "That request just changed. Refresh and try again.";

    /// <summary>The error when the other user doesn't exist. Public so an API can map it to "not found".</summary>
    public const string UnknownUserError = "That user doesn't exist.";

    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    /// <summary>The pair in stored order: smaller id first.</summary>
    private static (int A, int B) Pair(int x, int y) => x < y ? (x, y) : (y, x);

    public async Task<FriendSearchResult> SearchAsync(int userId, string query)
    {
        query = query.Trim();
        if (query.Length < MinSearchLength)
            return new(false, [], $"Type at least {MinSearchLength} characters.");

        await using var s = await store.OpenAsync();
        var users = await s.SearchUsersAsync(query, userId, MaxSearchResults);
        var links = (await s.GetFriendshipsAsync(userId)).ToDictionary(l => l.Other.Id, l => l.Friendship);

        var found = users.Select(u => new FoundUser(u.Id, u.Username,
            !links.TryGetValue(u.Id, out var f) ? FriendRelation.None
            : f.Status == FriendshipStatus.Accepted ? FriendRelation.Friend
            : f.RequestedById == userId ? FriendRelation.RequestSent
            : FriendRelation.RequestReceived)).ToList();
        return new(true, found);
    }

    /// <summary>Sends a request, or accepts theirs if the other user already sent you one.</summary>
    public async Task<FriendResult> SendRequestAsync(int userId, int otherId)
    {
        if (userId == otherId) return new(false, "You can't add yourself.");

        await using var s = await store.OpenAsync();
        if (await s.FindUserAsync(otherId) is null) return new(false, UnknownUserError);

        var (a, b) = Pair(userId, otherId);
        if (await s.GetFriendshipAsync(a, b) is { } existing)
        {
            if (existing.Status == FriendshipStatus.Accepted) return new(false, "You're already friends.");
            if (existing.RequestedById == userId) return new(false, "You already sent a request.");
            return await AcceptAsync(s, userId, otherId, existing);
        }

        var mine = await s.GetFriendshipsAsync(userId);
        if (mine.Count(l => l.Friendship.Status == FriendshipStatus.Accepted) >= MaxFriends)
            return new(false, $"You can have at most {MaxFriends} friends.");
        if (mine.Count(l => l.Friendship.IsOutgoingFor(userId)) >= MaxPendingOutgoing)
            return new(false, $"You can have at most {MaxPendingOutgoing} requests waiting. Cancel one first.");

        await s.AddFriendshipAsync(new FriendshipRecord(a, b, userId, FriendshipStatus.Pending, Now, null));
        return await CommitAsync(s, new FriendResult(true));
    }

    /// <summary>Only the user who received the request can accept it.</summary>
    public async Task<FriendResult> AcceptAsync(int userId, int otherId)
    {
        await using var s = await store.OpenAsync();
        var (a, b) = Pair(userId, otherId);
        var f = await s.GetFriendshipAsync(a, b);
        if (f is null || f.Status != FriendshipStatus.Pending) return new(false, "There's no request to accept.");
        if (f.RequestedById == userId) return new(false, "You can't accept your own request.");
        return await AcceptAsync(s, userId, otherId, f);
    }

    private async Task<FriendResult> AcceptAsync(ISocialSession s, int userId, int otherId, FriendshipRecord request)
    {
        if (await FriendCountAsync(s, userId) >= MaxFriends)
            return new(false, $"You can have at most {MaxFriends} friends.");
        if (await FriendCountAsync(s, otherId) >= MaxFriends)
            return new(false, "They already have the most friends allowed.");

        await s.UpdateFriendshipAsync(request with { Status = FriendshipStatus.Accepted, AcceptedAt = Now });
        return await CommitAsync(s, new FriendResult(true, BecameFriends: true));
    }

    /// <summary>The receiver turns the request down. The sender isn't told; the request just disappears.</summary>
    public Task<FriendResult> DeclineAsync(int userId, int otherId) =>
        DeleteAsync(userId, otherId, f => f.IsIncomingFor(userId), "There's no request to decline.");

    /// <summary>The sender takes back their own request.</summary>
    public Task<FriendResult> CancelAsync(int userId, int otherId) =>
        DeleteAsync(userId, otherId, f => f.IsOutgoingFor(userId), "There's no request to cancel.");

    /// <summary>Either friend ends the friendship.</summary>
    public Task<FriendResult> RemoveAsync(int userId, int otherId) =>
        DeleteAsync(userId, otherId, f => f.Status == FriendshipStatus.Accepted, "You're not friends.");

    private async Task<FriendResult> DeleteAsync(int userId, int otherId, Func<FriendshipRecord, bool> allowed, string notFound)
    {
        await using var s = await store.OpenAsync();
        var (a, b) = Pair(userId, otherId);
        var f = await s.GetFriendshipAsync(a, b);
        if (f is null || !allowed(f)) return new(false, notFound);

        await s.DeleteFriendshipAsync(a, b);
        return await CommitAsync(s, new FriendResult(true));
    }

    /// <summary>Accepted friends, by username.</summary>
    public async Task<IReadOnlyList<Friend>> GetFriendsAsync(int userId)
    {
        await using var s = await store.OpenAsync();
        return (await s.GetFriendshipsAsync(userId))
            .Where(l => l.Friendship.Status == FriendshipStatus.Accepted)
            .Select(l => new Friend(l.Other.Id, l.Other.Username, l.Friendship.AcceptedAt ?? l.Friendship.CreatedAt))
            .OrderBy(f => f.Username, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<FriendRequests> GetRequestsAsync(int userId)
    {
        await using var s = await store.OpenAsync();
        var links = await s.GetFriendshipsAsync(userId);

        List<FriendRequest> Pick(Func<FriendshipRecord, bool> which) => links
            .Where(l => which(l.Friendship))
            .OrderByDescending(l => l.Friendship.CreatedAt)
            .Select(l => new FriendRequest(l.Other.Id, l.Other.Username, l.Friendship.CreatedAt))
            .ToList();

        return new(Pick(f => f.IsIncomingFor(userId)), Pick(f => f.IsOutgoingFor(userId)));
    }

    /// <summary>Incoming requests, for the badge on Home.</summary>
    public async Task<int> IncomingCountAsync(int userId)
    {
        await using var s = await store.OpenAsync();
        return (await s.GetFriendshipsAsync(userId)).Count(l => l.Friendship.IsIncomingFor(userId));
    }

    private static async Task<int> FriendCountAsync(ISocialSession s, int userId) =>
        (await s.GetFriendshipsAsync(userId)).Count(l => l.Friendship.Status == FriendshipStatus.Accepted);

    private static async Task<FriendResult> CommitAsync(ISocialSession s, FriendResult ok)
    {
        try { await s.CommitAsync(); }
        catch (SocialStoreConflictException) { return new(false, ConflictError); }
        return ok;
    }
}
