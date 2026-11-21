using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using GymApp.Core;

namespace GymApp.Social;

/// <summary>Friend requests and friendships, as the view models call them. <see cref="FriendService"/> runs the rules
/// itself (the TestBench's local mode and the server); the app's remote version asks the server, which runs them.
/// In remote mode every user id in the results is a server id, except the <c>userId</c> argument: that is always the
/// signed-in user, so compare rows with <see cref="LeaderboardRow.IsYou"/>, never with a local id.</summary>
public interface IFriendService
{
    Task<FriendSearchResult> SearchAsync(int userId, string query);
    Task<FriendResult> SendRequestAsync(int userId, int otherId);
    Task<FriendResult> AcceptAsync(int userId, int otherId);
    Task<FriendResult> DeclineAsync(int userId, int otherId);
    Task<FriendResult> CancelAsync(int userId, int otherId);
    Task<FriendResult> RemoveAsync(int userId, int otherId);
    /// <summary>The remote version returns a <see cref="FriendList"/>, which says whether it came from the cache.</summary>
    Task<IReadOnlyList<Friend>> GetFriendsAsync(int userId);
    Task<FriendRequests> GetRequestsAsync(int userId);
    Task<int> IncomingCountAsync(int userId);
    Task<FriendResult> BlockAsync(int userId, int otherId);
    Task<FriendResult> UnblockAsync(int userId, int otherId);

    /// <summary>Your invite code for the QR code and link, made on first use (Phase 21).</summary>
    Task<InviteCodeResult> GetInviteCodeAsync(int userId);

    /// <summary>A new invite code; the old QR code and link stop working.</summary>
    Task<InviteCodeResult> ResetInviteCodeAsync(int userId);

    /// <summary>Whose code this is, for "alex wants to be your friend".</summary>
    Task<InviteLookup> LookUpInviteAsync(int userId, string code);

    /// <summary>Accepts the invite: the two become friends at once.</summary>
    Task<FriendResult> AcceptInviteAsync(int userId, string code);
    /// <summary>The users you blocked, by username.</summary>
    Task<IReadOnlyList<BlockedUser>> GetBlockedAsync(int userId);
}

public interface ILeaderboardService
{
    /// <summary>The board, or null if the user doesn't exist.</summary>
    Task<LeaderboardView?> GetAsync(int userId, LeaderboardPeriod period, DateOnly? today = null);
}

/// <summary>A read that may have come from the cache: <see cref="IsStale"/> when the server couldn't be reached.
/// <see cref="FetchedAt"/> (UTC) is when the shown data was fetched; null if there was nothing cached yet.</summary>
public interface ICachedResult
{
    bool IsStale { get; }
    DateTime? FetchedAt { get; }
}

/// <summary>A list from a read that may have come from the cache. Local services return plain lists (always fresh).</summary>
public class CachedList<T>(IReadOnlyList<T> items, bool isStale = false, DateTime? fetchedAt = null)
    : IReadOnlyList<T>, ICachedResult
{
    public bool IsStale { get; } = isStale;
    public DateTime? FetchedAt { get; } = fetchedAt;
    public int Count => items.Count;
    public T this[int index] => items[index];
    public IEnumerator<T> GetEnumerator() => items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Accepted friends, plus where they came from.</summary>
public sealed class FriendList(IReadOnlyList<Friend> friends, bool isStale = false, DateTime? fetchedAt = null)
    : CachedList<Friend>(friends, isStale, fetchedAt);

/// <summary>A badge a friend earned (Phase 25): its id, tier (0 for a one-time badge) and when the tier was reached.</summary>
public sealed record SharedBadge(string Id, int Tier, DateOnly EarnedOn);

/// <summary>A friend's badges and featured badge id. Empty when they don't share them. <see cref="ICachedResult"/>:
/// stale when the server couldn't be reached and this came from the cache.</summary>
public sealed record FriendBadges(int UserId, IReadOnlyList<SharedBadge> Badges, string? Featured) : ICachedResult
{
    public bool IsStale { get; init; }
    public DateTime? FetchedAt { get; init; }
}

/// <summary>Badges between friends (Phase 25): a friend's badges, and how many people joined through your invite.
/// Your own badges go up with the stats (SocialSync), not through here.</summary>
public interface IBadgeShareService
{
    /// <summary>The friend's badges, or null when they aren't a friend (or nothing was ever fetched while offline).</summary>
    Task<FriendBadges?> GetFriendBadgesAsync(int userId, int friendId);

    /// <summary>How many people became the user's friend through their invite; null when it couldn't be fetched.</summary>
    Task<int?> GetAcceptedInvitesAsync(int userId);
}
