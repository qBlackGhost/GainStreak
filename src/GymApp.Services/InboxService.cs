using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
    private readonly SessionService _session;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    /// <summary>How often the badge reads the inbox while the app is open (backlog section 9).</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

    public InboxService(Func<IEnumerable<IInboxSource>> sources, InboxStore store, SessionService session,
                        Func<DateTime>? utcNow = null, TimeProvider? time = null)
    {
        _sources = sources;
        _store = store;
        _session = session;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _time = time ?? TimeProvider.System;
        session.Changed += () =>
        {
            UnreadCount = 0;
            RequestRefresh();   // a sign-in shows the new user's count at once
        };
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

    /// <summary>For the badge: reads the inbox and sets the count, without marking anything seen. Offline (the lists
    /// came from the cache) or failing, it keeps the last number instead of clearing it. One read at a time.</summary>
    public async Task RefreshUnreadAsync(int userId)
    {
        await _refreshGate.WaitAsync();
        try
        {
            var batches = new List<InboxBatch>();
            foreach (var source in _sources()) batches.Add(await source.GetAsync(userId));
            if (batches.Any(b => b.IsStale)) return;
            if (_session.CurrentUser?.Id != userId) return;   // signed out or switched meanwhile
            var seen = _store.Seen(userId);
            UnreadCount = batches.SelectMany(b => b.Items).Count(i => !seen.Contains(i.Key));
        }
        catch (Exception) { /* keep the last number */ }
        finally { _refreshGate.Release(); }
    }

    /// <summary><see cref="RefreshUnreadAsync"/> for the signed-in user, in the background. For sign-in, app start,
    /// the foreground, a social sync and the timer.</summary>
    public void RequestRefresh()
    {
        if (_session.CurrentUser is { } user) _ = RefreshUnreadAsync(user.Id);
    }

    private readonly Lock _timerLock = new();
    private CancellationTokenSource? _timerStop;

    /// <summary>Refreshes the badge every <see cref="RefreshInterval"/> while the app is open, unless already running.
    /// The phones stop it in the background.</summary>
    public void StartTimer()
    {
        CancellationToken token;
        lock (_timerLock)
        {
            if (_timerStop is not null) return;
            _timerStop = new CancellationTokenSource();
            token = _timerStop.Token;
        }
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(RefreshInterval, _time);
            try
            {
                while (await timer.WaitForNextTickAsync(token)) RequestRefresh();
            }
            catch (OperationCanceledException) { /* stopped */ }
        });
    }

    public void StopTimer()
    {
        lock (_timerLock)
        {
            _timerStop?.Cancel();
            _timerStop?.Dispose();
            _timerStop = null;
        }
    }

    public bool IsTimerRunning
    {
        get { lock (_timerLock) return _timerStop is not null; }
    }

    /// <summary>Everything in <paramref name="view"/> counts as seen; older keys are forgotten.</summary>
    public void MarkAllRead(int userId, InboxView view)
    {
        _store.SetSeen(userId, view.Items.Select(i => i.Key));
        UnreadCount = 0;
    }

    public void Record(int userId, InboxKind kind, string text) =>
        _store.AddHistory(userId, new InboxHistoryEntry(kind, text, _utcNow()));
}
