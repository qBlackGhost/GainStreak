using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GymApp.Social;

namespace GymApp.Services;

/// <summary>What an inbox item is; each kind has its own item record (shares: ShareService.cs).</summary>
public enum InboxKind { FriendRequest, SharedSplit, SharedWorkout }

/// <summary>One thing waiting in the inbox. <see cref="Key"/> is stable for the same item across reloads (it is how
/// "seen" is remembered); <see cref="At"/> is UTC. <see cref="IsNew"/> is set by <see cref="InboxService"/>.
/// Each kind is its own record so it can carry what its row needs.</summary>
public abstract record InboxItem(string Key, InboxKind Kind, int FromUserId, string FromUsername, DateTime At)
{
    public bool IsNew { get; init; }
}

/// <summary>Someone asked to be friends. <see cref="InboxItem.FromUserId"/> is the friend service's id (a server id
/// in remote mode): pass it back to Accept / Decline as it came.</summary>
public sealed record FriendRequestItem(int FromUserId, string FromUsername, DateTime At)
    : InboxItem($"friend:{FromUserId}:{At.Ticks}", InboxKind.FriendRequest, FromUserId, FromUsername, At);

/// <summary>A line under "Earlier": something done from the inbox, e.g. "You're now friends with ana". UTC.</summary>
public record InboxHistoryEntry(InboxKind Kind, string Text, DateTime At);

/// <summary>What one source has waiting, and whether it came from the cache.</summary>
public record InboxBatch(IReadOnlyList<InboxItem> Items, bool IsStale = false, DateTime? FetchedAt = null);

/// <summary>Where inbox items come from: friend requests, and the splits and workouts friends share.</summary>
public interface IInboxSource
{
    Task<InboxBatch> GetAsync(int userId);
}

public class FriendRequestInboxSource(IFriendService friends) : IInboxSource
{
    public async Task<InboxBatch> GetAsync(int userId)
    {
        var requests = await friends.GetRequestsAsync(userId);
        return new InboxBatch(
            requests.Incoming.Select(r => (InboxItem)new FriendRequestItem(r.UserId, r.Username, r.SentAt)).ToList(),
            requests.IsStale, requests.FetchedAt);
    }
}

/// <summary>The inbox as the screen shows it: waiting items newest first, then the history.</summary>
public record InboxView(IReadOnlyList<InboxItem> Items, IReadOnlyList<InboxHistoryEntry> History) : ICachedResult
{
    public bool IsStale { get; init; }
    public DateTime? FetchedAt { get; init; }
    public int NewCount => Items.Count(i => i.IsNew);
}

/// <summary>Per-user, on this device: which items were marked read, and the "Earlier" history. A small JSON file per
/// user in the data directory; the server only holds the items themselves.</summary>
public class InboxStore(string directory)
{
    public const int MaxHistory = 20;
    public static readonly TimeSpan HistoryKeptFor = TimeSpan.FromDays(30);

    private record State(List<string> Seen, List<InboxHistoryEntry> History);

    private string PathFor(int userId) => Path.Combine(directory, $"inbox-{userId}.json");

    private State Load(int userId)
    {
        try
        {
            var path = PathFor(userId);
            if (File.Exists(path) && JsonSerializer.Deserialize<State>(File.ReadAllText(path)) is { } s)
                return new State(s.Seen ?? [], s.History ?? []);
        }
        catch (Exception) { }   // a broken file just means nothing seen yet
        return new State([], []);
    }

    private void Save(int userId, State state)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(PathFor(userId), JsonSerializer.Serialize(state));
    }

    public IReadOnlySet<string> Seen(int userId) => Load(userId).Seen.ToHashSet();

    /// <summary>Remembers exactly these keys as seen; keys of items that are gone are dropped with it.</summary>
    public void SetSeen(int userId, IEnumerable<string> keys)
    {
        var s = Load(userId);
        Save(userId, s with { Seen = keys.Distinct().ToList() });
    }

    /// <summary>The history newest first, without entries older than <see cref="HistoryKeptFor"/>.</summary>
    public IReadOnlyList<InboxHistoryEntry> History(int userId, DateTime nowUtc) =>
        Load(userId).History.Where(h => nowUtc - h.At <= HistoryKeptFor).OrderByDescending(h => h.At).ToList();

    public void AddHistory(int userId, InboxHistoryEntry entry)
    {
        var s = Load(userId);
        var history = s.History.Prepend(entry)
            .Where(h => entry.At - h.At <= HistoryKeptFor)
            .OrderByDescending(h => h.At)
            .Take(MaxHistory)
            .ToList();
        Save(userId, s with { History = history });
    }
}

/// <summary>Merges the sources into one inbox, marks what's new, and keeps the unread count the sidebar badge shows.
/// One instance for the app, so every screen's sidebar sees the same count.</summary>
public class InboxService
{
    private readonly Func<IEnumerable<IInboxSource>> _sources;
    private readonly InboxStore _store;
    private readonly Func<DateTime> _utcNow;

    public InboxService(Func<IEnumerable<IInboxSource>> sources, InboxStore store, SessionService session,
                        Func<DateTime>? utcNow = null)
    {
        _sources = sources;
        _store = store;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        session.Changed += () => UnreadCount = 0;
    }

    private int _unread;

    /// <summary>New items as of the last load; 0 until something loads the inbox.</summary>
    public int UnreadCount
    {
        get => _unread;
        private set
        {
            if (_unread == value) return;
            _unread = value;
            UnreadChanged?.Invoke();
        }
    }

    public event Action? UnreadChanged;

    public async Task<InboxView> GetAsync(int userId)
    {
        var batches = new List<InboxBatch>();
        foreach (var source in _sources()) batches.Add(await source.GetAsync(userId));

        var seen = _store.Seen(userId);
        var items = batches.SelectMany(b => b.Items)
            .Select(i => i with { IsNew = !seen.Contains(i.Key) })
            .OrderByDescending(i => i.At)
            .ToList();
        var stale = batches.FirstOrDefault(b => b.IsStale);
        var view = new InboxView(items, _store.History(userId, _utcNow()))
        {
            IsStale = stale is not null,
            FetchedAt = stale?.FetchedAt
        };
        UnreadCount = view.NewCount;
        return view;
    }

    /// <summary>For screens that only need the badge.</summary>
    public async Task RefreshUnreadAsync(int userId) => await GetAsync(userId);

    /// <summary>Everything in <paramref name="view"/> counts as seen; older keys are forgotten.</summary>
    public void MarkAllRead(int userId, InboxView view)
    {
        _store.SetSeen(userId, view.Items.Select(i => i.Key));
        UnreadCount = 0;
    }

    public void Record(int userId, InboxKind kind, string text) =>
        _store.AddHistory(userId, new InboxHistoryEntry(kind, text, _utcNow()));
}
