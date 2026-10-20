using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Core;
using GymApp.Services;
using GymApp.Social;

namespace GymApp.Desktop.ViewModels;

/// <summary>The "you're offline" line for a read that came from the cache, or null when it's fresh.</summary>
internal static class StaleText
{
    public static string? For(ICachedResult? result)
    {
        if (result is not { IsStale: true }) return null;
        if (result.FetchedAt is not { } at) return "You're offline. Friends will show when you're back online.";
        var local = at.ToLocalTime();
        var when = local.Date == DateTime.Now.Date
            ? local.ToString("HH:mm", CultureInfo.InvariantCulture)
            : local.ToString("d MMM, HH:mm", CultureInfo.InvariantCulture);
        return $"You're offline. Last updated {when}.";
    }
}

/// <summary>"CR" for an avatar: the first two letters of a username.</summary>
internal static class Initials
{
    public static string Of(string username)
    {
        var letters = new string(username.Where(char.IsLetter).Take(2).ToArray());
        return letters.Length > 0 ? letters.ToUpperInvariant() : username[..Math.Min(2, username.Length)].ToUpperInvariant();
    }
}

/// <summary>One row of the leaderboard. Ranking comes from <see cref="Leaderboard"/>; nothing is computed here.</summary>
public record FriendsBoardRowViewModel(LeaderboardRow Row)
{
    public string RankText => $"#{Row.Rank}";
    public bool IsFirst => Row.Rank == 1;
    public string Username => Row.IsYou ? $"{Row.Username} (you)" : Row.Username;
    public string Initials => ViewModels.Initials.Of(Row.Username);
    public string LevelText => $"Level {Row.Level.Number} · {Row.Level.Name}";
    public string PointsText => $"{Row.Points.ToString("N0", CultureInfo.InvariantCulture)} pts";
    public string StreakText => Row.CurrentStreak.ToString(CultureInfo.InvariantCulture);
    public bool HasStreak => Row.CurrentStreak > 0;
    public bool IsYou => Row.IsYou;
}

/// <summary>One accepted friend. Remove asks for confirmation first: the button turns into "Confirm remove".</summary>
public partial class FriendRowViewModel(Friend friend, string? level, FriendsViewModel owner) : ObservableObject
{
    public int UserId => friend.UserId;
    public string Username => friend.Username;
    public string Initials => ViewModels.Initials.Of(friend.Username);
    public string SinceText => (level is null ? "" : level + " · ")
                               + $"since {friend.Since.ToLocalTime().ToString("MMM yyyy", CultureInfo.InvariantCulture)}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemoveLabel))]
    public partial bool IsConfirmingRemove { get; set; }

    public string RemoveLabel => IsConfirmingRemove ? "Confirm remove" : "Remove";

    [RelayCommand]
    private Task Remove()
    {
        if (IsConfirmingRemove) return owner.RemoveAsync(this);
        IsConfirmingRemove = true;
        return Task.CompletedTask;
    }

    [RelayCommand] private void CancelRemove() => IsConfirmingRemove = false;
}

/// <summary>A pending request: incoming ones can be accepted or declined, outgoing ones cancelled.</summary>
public partial class RequestRowViewModel(FriendRequest request, bool incoming, FriendsViewModel owner) : ObservableObject
{
    public int UserId => request.UserId;
    public string Username => request.Username;
    public string Initials => ViewModels.Initials.Of(request.Username);
    public bool IsIncoming => incoming;
    public bool IsOutgoing => !incoming;
    public string StatusText => incoming ? "Wants to be friends" : "Request sent · waiting";
    public string SentText => (incoming ? "Sent you a request " : "Requested ")
                              + request.SentAt.ToLocalTime().ToString("d MMM", CultureInfo.InvariantCulture);

    [RelayCommand] private Task Accept() => owner.AcceptAsync(UserId);
    [RelayCommand] private Task Decline() => owner.DeclineAsync(UserId);
    [RelayCommand] private Task Cancel() => owner.CancelAsync(UserId);
}

/// <summary>A search result. The button follows the relationship: Add, Requested, Accept or Friends.</summary>
public partial class FoundUserViewModel(FoundUser user, FriendsViewModel owner) : ObservableObject
{
    public string Username => user.Username;
    public string Initials => ViewModels.Initials.Of(user.Username);
    public bool IsAccept => user.Relation == FriendRelation.RequestReceived;

    public string ActionLabel => user.Relation switch
    {
        FriendRelation.Friend => "Friends",
        FriendRelation.RequestSent => "Requested",
        FriendRelation.RequestReceived => "Accept",
        _ => "Add"
    };

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task Act() => owner.SendRequestAsync(user.UserId);   // sending to someone who asked you accepts theirs
    private bool CanAct() => user.Relation is FriendRelation.None or FriendRelation.RequestReceived;
}

/// <summary>The friends screen: the leaderboard (this week or all time), your friends, the username search and
/// pending requests. Only reads results from the friend and leaderboard services (local or the server's). User ids in
/// the rows are the service's own (server ids in remote mode): they're passed back as they came, never compared with
/// the signed-in user's local id.</summary>
public partial class FriendsViewModel : ViewModelBase
{
    private readonly IFriendService _friends;
    private readonly ILeaderboardService _board;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;

    public FriendsViewModel(IFriendService friends, ILeaderboardService board, SessionService session, Func<MainViewModel> shell)
    {
        _friends = friends;
        _board = board;
        _session = session;
        _shell = shell;
        _ = LoadAsync();
    }

    public ObservableCollection<FriendsBoardRowViewModel> Board { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsThisWeek))]
    public partial bool IsAllTime { get; set; }

    public bool IsThisWeek => !IsAllTime;

    [ObservableProperty] public partial string PeriodText { get; set; } = "";
    [ObservableProperty] public partial string FriendCountText { get; set; } = "";
    [ObservableProperty] public partial string SentCountText { get; set; } = "";
    [ObservableProperty] public partial int IncomingCount { get; set; }

    [RelayCommand]
    private async Task ShowThisWeekAsync()
    {
        IsAllTime = false;
        await LoadBoardAsync();
    }

    [RelayCommand]
    private async Task ShowAllTimeAsync()
    {
        IsAllTime = true;
        await LoadBoardAsync();
    }

    /// <summary>The board, and each friend's level for their row. A failure only empties the board.</summary>
    private async Task<IReadOnlyDictionary<int, LevelInfo>> LoadBoardAsync()
    {
        var levels = new Dictionary<int, LevelInfo>();
        if (UserId is not { } me) return levels;
        try
        {
            var period = IsAllTime ? LeaderboardPeriod.AllTime : LeaderboardPeriod.ThisWeek;
            var view = await _board.GetAsync(me, period);
            if (view is null || IsAllTime != (view.Period == LeaderboardPeriod.AllTime)) return levels;   // toggled meanwhile
            BoardStale = StaleText.For(view);
            Board.Clear();
            foreach (var r in view.Rows) Board.Add(new FriendsBoardRowViewModel(r));
            foreach (var r in view.Rows) levels[r.UserId] = r.Level;
            PeriodText = IsAllTime ? "All time" : Leaderboard.WeekTitle(view.WeekStart);
        }
        catch (Exception)
        {
            Board.Clear();
            Error = "Could not load the leaderboard.";
        }
        return levels;
    }

    public ObservableCollection<FriendRowViewModel> Friends { get; } = new();
    public ObservableCollection<RequestRowViewModel> Incoming { get; } = new();
    public ObservableCollection<RequestRowViewModel> Outgoing { get; } = new();
    public ObservableCollection<FoundUserViewModel> Results { get; } = new();

    [ObservableProperty] public partial string FriendsTitle { get; set; } = "Friends";
    [ObservableProperty] public partial bool HasNoFriends { get; set; }
    [ObservableProperty] public partial bool HasIncoming { get; set; }
    [ObservableProperty] public partial bool HasOutgoing { get; set; }
    [ObservableProperty] public partial bool HasNoRequests { get; set; }
    [ObservableProperty] public partial string Query { get; set; } = "";
    [ObservableProperty] public partial string? SearchHint { get; set; }
    [ObservableProperty] public partial string? Notice { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }

    /// <summary>"You're offline. Last updated 14:05." over the lists, when they came from the cache.</summary>
    [ObservableProperty] public partial string? StaleNotice { get; set; }
    private string? BoardStale { get; set; }

    private int? UserId => _session.CurrentUser?.Id;

    private async Task LoadAsync()
    {
        if (UserId is not { } me) return;
        try
        {
            var levels = await LoadBoardAsync();
            var friends = await _friends.GetFriendsAsync(me);
            Friends.Clear();
            foreach (var f in friends)
                Friends.Add(new FriendRowViewModel(f,
                    levels.TryGetValue(f.UserId, out var l) ? $"Level {l.Number} · {l.Name}" : null, this));
            FriendsTitle = friends.Count == 0 ? "Friends" : $"Friends ({friends.Count}/{FriendService.MaxFriends})";
            FriendCountText = $"{friends.Count} / {FriendService.MaxFriends} friends";
            HasNoFriends = friends.Count == 0;

            var requests = await _friends.GetRequestsAsync(me);
            Incoming.Clear();
            foreach (var r in requests.Incoming) Incoming.Add(new RequestRowViewModel(r, true, this));
            Outgoing.Clear();
            foreach (var r in requests.Outgoing) Outgoing.Add(new RequestRowViewModel(r, false, this));
            HasIncoming = Incoming.Count > 0;
            HasOutgoing = Outgoing.Count > 0;
            HasNoRequests = !HasIncoming && !HasOutgoing;
            IncomingCount = Incoming.Count;
            StaleNotice = StaleText.For(friends as ICachedResult) ?? StaleText.For(requests) ?? BoardStale;
            SentCountText = $"{Outgoing.Count} / {FriendService.MaxPendingOutgoing} requests sent";
        }
        catch (Exception)
        {
            Error = "Could not load your friends.";
        }
    }

    // Button or Enter, like the city search
    [RelayCommand]
    private async Task SearchAsync()
    {
        try
        {
            if (UserId is not { } me) return;
            Notice = null;
            var r = await _friends.SearchAsync(me, Query);
            Results.Clear();
            if (!r.Success) { SearchHint = r.Error; return; }

            foreach (var u in r.Users) Results.Add(new FoundUserViewModel(u, this));
            SearchHint = r.Users.Count == 0 ? $"No one found starting with \"{Query.Trim()}\"." : null;

        }
        catch (Exception)
        {
            SearchHint = "Could not search for users.";
        }
    }

    internal Task SendRequestAsync(int otherId) =>
        RunAsync(me => _friends.SendRequestAsync(me, otherId), r => r.BecameFriends ? "You're now friends." : "Request sent.");

    internal Task AcceptAsync(int otherId) =>
        RunAsync(me => _friends.AcceptAsync(me, otherId), _ => "You're now friends.");

    internal Task DeclineAsync(int otherId) =>
        RunAsync(me => _friends.DeclineAsync(me, otherId), _ => null);

    internal Task CancelAsync(int otherId) =>
        RunAsync(me => _friends.CancelAsync(me, otherId), _ => null);

    internal Task RemoveAsync(FriendRowViewModel row) =>
        RunAsync(me => _friends.RemoveAsync(me, row.UserId), _ => $"{row.Username} was removed from your friends.");

    /// <summary>Runs a write, then reloads the lists and the search results so every button shows the new state.</summary>
    private async Task RunAsync(Func<int, Task<FriendResult>> write, Func<FriendResult, string?> notice)
    {
        if (UserId is not { } me) return;
        try
        {
            var r = await write(me);
            Error = r.Success ? null : r.Error;
            Notice = r.Success ? notice(r) : null;
        }
        catch (Exception)
        {
            Error = "Something went wrong. Try again.";
        }
        await LoadAsync();
        if (Results.Count > 0) await RefreshResultsAsync(me);
    }

    private async Task RefreshResultsAsync(int me)
    {
        try
        {
            var r = await _friends.SearchAsync(me, Query);
            if (!r.Success) return;
            Results.Clear();
            foreach (var u in r.Users) Results.Add(new FoundUserViewModel(u, this));

        }
        catch (Exception)
        {
            Error = "Could not refresh the search results.";
        }
    }

    [RelayCommand]
    private void Back() => _shell().ShowHome();
}
