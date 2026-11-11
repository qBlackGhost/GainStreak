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

namespace GymApp.App.ViewModels;

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
    /// <summary>Phones (the canvas): the rank without "#", the name with a YOU tag, the points over "pts".</summary>
    public string RankNumber => Row.Rank.ToString(CultureInfo.InvariantCulture);
    public string Name => Row.Username;
    public string PointsNumber => Row.Points.ToString("N0", CultureInfo.InvariantCulture);
    public bool IsFirst => Row.Rank == 1;
    public string Username => Row.IsYou ? $"{Row.Username} (you)" : Row.Username;
    public string Initials => ViewModels.Initials.Of(Row.Username);
    public string? Avatar => Row.Avatar;
    public int LevelNumber => Row.Level.Number;
    public string LevelText => $"Level {Row.Level.Number} · {Row.Level.Name}";
    public string PointsText => $"{Row.Points.ToString("N0", CultureInfo.InvariantCulture)} pts";
    public string StreakText => Row.CurrentStreak.ToString(CultureInfo.InvariantCulture);
    public bool HasStreak => Row.CurrentStreak > 0;
    public bool IsYou => Row.IsYou;
}

/// <summary>One accepted friend. Remove asks for confirmation first: the button turns into "Confirm remove".</summary>
public partial class FriendRowViewModel(Friend friend, LeaderboardRow? row, FriendsViewModel owner) : ObservableObject
{
    public int UserId => friend.UserId;
    public string Username => friend.Username;
    public string Initials => ViewModels.Initials.Of(friend.Username);
    public string? Avatar => row?.Avatar;
    public int LevelNumber => row?.Level.Number ?? 1;
    public string SinceText => (row is null ? "" : $"Level {row.Level.Number} · {row.Level.Name} · ")
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
    [RelayCommand] private void Report() => owner.OpenReport(UserId, Username);
}

/// <summary>Someone you blocked. They only come back through Unblock: they don't show in search.</summary>
public partial class BlockedRowViewModel(BlockedUser user, FriendsViewModel owner) : ObservableObject
{
    public int UserId => user.UserId;
    public string Username => user.Username;
    public string Initials => ViewModels.Initials.Of(user.Username);

    [RelayCommand] private Task Unblock() => owner.UnblockAsync(this);
}

/// <summary>A request you sent, waiting for an answer; it can be cancelled. Requests you get are in the inbox.</summary>
public partial class RequestRowViewModel(FriendRequest request, FriendsViewModel owner) : ObservableObject
{
    public int UserId => request.UserId;
    public string Username => request.Username;
    public string Initials => ViewModels.Initials.Of(request.Username);
    public string StatusText => "Request sent · waiting";

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

    [RelayCommand] private void Report() => owner.OpenReport(user.UserId, user.Username);
}

/// <summary>The friends screen: the leaderboard (this week or all time), your friends, the username search and
/// the requests you sent (the ones you get are in the inbox). Only reads results from the friend and leaderboard services (local or the server's). User ids in
/// the rows are the service's own (server ids in remote mode): they're passed back as they came, never compared with
/// the signed-in user's local id.</summary>
public partial class FriendsViewModel : ViewModelBase, IClosesOnBack
{
    /// <summary>Back closes the report popup or the QR sheet first.</summary>
    public bool CloseOnBack()
    {
        if (Report.IsOpen) { Report.IsOpen = false; return true; }
        if (Qr.IsOpen) { Qr.CloseCommand.Execute(null); return true; }
        return false;
    }

    private readonly IFriendService _friends;
    private readonly ILeaderboardService _board;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;
    private readonly InboxService _inbox;

    public FriendsViewModel(IFriendService friends, ILeaderboardService board, SessionService session, Func<MainViewModel> shell,
                            InboxService inbox, ReportUserDialogViewModel report, FriendQrViewModel qr)
    {
        Report = report;
        Qr = qr;
        _friends = friends;
        _inbox = inbox;
        _board = board;
        _session = session;
        _shell = shell;
        _ = LoadAsync();
    }

    public ReportUserDialogViewModel Report { get; }

    /// <summary>"Your QR code" (Phase 21).</summary>
    public FriendQrViewModel Qr { get; }

    [RelayCommand]
    private Task ShowQrAsync() => Qr.OpenAsync();

    // ===== The invite card (Phase 21): a scanned QR code or an opened invite link =====

    public const string OfflineInviteText = "Couldn't check this invite. Try again when you're online.";

    private string? _inviteCode;
    private string? _inviterName;

    [ObservableProperty] public partial bool HasInvite { get; private set; }
    [ObservableProperty] public partial bool IsInviteChecking { get; private set; }

    /// <summary>"radu99 wants to be your friend", with Not now and Accept.</summary>
    [ObservableProperty] public partial bool IsInviteAsk { get; private set; }
    [ObservableProperty] public partial string InviterName { get; private set; } = "";
    [ObservableProperty] public partial string InviterInitials { get; private set; } = "";

    /// <summary>A result or a refusal in the card, with one button (and Try again when offline).</summary>
    [ObservableProperty] public partial bool IsInviteMessage { get; private set; }
    [ObservableProperty] public partial string? InviteMessage { get; private set; }
    /// <summary>The message's icon: a resource key (IcCheck, IcInfoCircle, IcLinkOff, IcBlock, IcWifiOff).</summary>
    [ObservableProperty] public partial string InviteIcon { get; private set; } = "IcInfoCircle";
    [ObservableProperty] public partial bool IsInviteGood { get; private set; }
    [ObservableProperty] public partial bool CanRetryInvite { get; private set; }
    [ObservableProperty] public partial string InviteOkLabel { get; private set; } = "OK";

    /// <summary>The shell hands over the code of a link the phone opened; the card checks whose it is.</summary>
    public Task ShowInviteAsync(string code)
    {
        _inviteCode = code;
        HasInvite = true;
        return CheckInviteAsync();
    }

    private void InviteStep(bool checking = false, bool ask = false)
    {
        IsInviteChecking = checking;
        IsInviteAsk = ask;
        IsInviteMessage = !checking && !ask;
    }

    private void InviteMessageShow(string text, string icon, string ok = "OK", bool good = false, bool retry = false)
    {
        InviteMessage = text;
        InviteIcon = icon;
        InviteOkLabel = ok;
        IsInviteGood = good;
        CanRetryInvite = retry;
        InviteStep();
    }

    private void InviteRefused(string? error)
    {
        if (error == GymApp.Client.ApiResult.OfflineMessage)
            InviteMessageShow(OfflineInviteText, "IcWifiOff", ok: "Not now", retry: true);
        else if (error == FriendService.UnknownInviteError)
            InviteMessageShow(error, "IcLinkOff");
        else if (error is not null && error.StartsWith("You blocked", StringComparison.Ordinal))
            InviteMessageShow(error, "IcBlock");
        else
            InviteMessageShow(error ?? "Something went wrong. Try again.", "IcInfoCircle");
    }

    private async Task CheckInviteAsync()
    {
        if (UserId is not { } me || _inviteCode is not { } code) return;
        InviteStep(checking: true);
        try
        {
            var r = await _friends.LookUpInviteAsync(me, code);
            if (!r.Success) { InviteRefused(r.Error); return; }
            _inviterName = InviterName = r.Inviter!.Username;
            InviterInitials = Initials(r.Inviter.Username);
            if (r.Relation == FriendRelation.Friend) InviteMessageShow($"You're already friends with {r.Inviter.Username}.", "IcInfoCircle");
            else InviteStep(ask: true);
        }
        catch (Exception)
        {
            InviteMessageShow("Could not check the invite. Try again.", "IcInfoCircle", ok: "Not now", retry: true);
        }
    }

    [RelayCommand]
    private async Task AcceptInviteAsync()
    {
        if (UserId is not { } me || _inviteCode is not { } code) return;
        InviteStep(checking: true);
        try
        {
            var r = await _friends.AcceptInviteAsync(me, code);
            if (r.Success) InviteMessageShow($"You and {_inviterName} are now friends.", "IcCheck", ok: "Done", good: true);
            else { InviteRefused(r.Error); return; }
        }
        catch (Exception)
        {
            InviteMessageShow("Something went wrong. Try again.", "IcInfoCircle", ok: "Not now", retry: true);
            return;
        }
        await ReloadAsync(me);
    }

    [RelayCommand] private Task RetryInviteAsync() => CheckInviteAsync();

    [RelayCommand] private void DismissInvite() => HasInvite = false;

    private static string Initials(string name)
    {
        var letters = new string(name.Where(char.IsLetter).Take(2).ToArray());
        return (letters.Length > 0 ? letters : name[..Math.Min(2, name.Length)]).ToUpperInvariant();
    }
    public ObservableCollection<FriendsBoardRowViewModel> Board { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsThisWeek))]
    public partial bool IsAllTime { get; set; }

    public bool IsThisWeek => !IsAllTime;

    [ObservableProperty] public partial string PeriodText { get; set; } = "";
    [ObservableProperty] public partial string FriendCountText { get; set; } = "";
    [ObservableProperty] public partial string SentCountText { get; set; } = "";
    /// <summary>Phones: "1 / 20 sent", beside the card's title.</summary>
    [ObservableProperty] public partial string SentShortText { get; set; } = "";

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

    /// <summary>The board, and each friend's row of it for their level and avatar. A failure only empties the board.</summary>
    private async Task<IReadOnlyDictionary<int, LeaderboardRow>> LoadBoardAsync()
    {
        var rows = new Dictionary<int, LeaderboardRow>();
        if (UserId is not { } me) return rows;
        try
        {
            var period = IsAllTime ? LeaderboardPeriod.AllTime : LeaderboardPeriod.ThisWeek;
            var view = await _board.GetAsync(me, period);
            if (view is null || IsAllTime != (view.Period == LeaderboardPeriod.AllTime)) return rows;   // toggled meanwhile
            BoardStale = StaleText.For(view);
            Board.Clear();
            foreach (var r in view.Rows) Board.Add(new FriendsBoardRowViewModel(r));
            foreach (var r in view.Rows) rows[r.UserId] = r;
            PeriodText = IsAllTime ? "All time" : Leaderboard.WeekTitle(view.WeekStart);
        }
        catch (Exception)
        {
            Board.Clear();
            Error = "Could not load the leaderboard.";
        }
        return rows;
    }

    public ObservableCollection<FriendRowViewModel> Friends { get; } = new();
    public ObservableCollection<RequestRowViewModel> Outgoing { get; } = new();
    public ObservableCollection<FoundUserViewModel> Results { get; } = new();
    public ObservableCollection<BlockedRowViewModel> Blocked { get; } = new();

    [ObservableProperty] public partial string FriendsTitle { get; set; } = "Friends";
    [ObservableProperty] public partial bool HasNoFriends { get; set; }
    [ObservableProperty] public partial bool HasOutgoing { get; set; }
    [ObservableProperty] public partial bool HasBlocked { get; set; }
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
            var board = await LoadBoardAsync();
            var friends = await _friends.GetFriendsAsync(me);
            Friends.Clear();
            foreach (var f in friends)
                Friends.Add(new FriendRowViewModel(f, board.GetValueOrDefault(f.UserId), this));
            FriendsTitle = friends.Count == 0 ? "Friends" : $"Friends ({friends.Count}/{FriendService.MaxFriends})";
            FriendCountText = $"{friends.Count} / {FriendService.MaxFriends} friends";
            HasNoFriends = friends.Count == 0;

            var requests = await _friends.GetRequestsAsync(me);
            Outgoing.Clear();
            foreach (var r in requests.Outgoing) Outgoing.Add(new RequestRowViewModel(r, this));
            HasOutgoing = Outgoing.Count > 0;

            var blocked = await _friends.GetBlockedAsync(me);
            Blocked.Clear();
            foreach (var b in blocked) Blocked.Add(new BlockedRowViewModel(b, this));
            HasBlocked = Blocked.Count > 0;

            StaleNotice = StaleText.For(friends as ICachedResult) ?? StaleText.For(requests) ?? BoardStale;
            SentCountText = $"{Outgoing.Count} / {FriendService.MaxPendingOutgoing} requests sent";
            SentShortText = $"{Outgoing.Count} / {FriendService.MaxPendingOutgoing} sent";
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

    internal Task CancelAsync(int otherId) =>
        RunAsync(me => _friends.CancelAsync(me, otherId), _ => null);

    internal Task RemoveAsync(FriendRowViewModel row) =>
        RunAsync(me => _friends.RemoveAsync(me, row.UserId), _ => $"{row.Username} was removed from your friends.");

    internal Task UnblockAsync(BlockedRowViewModel row) =>
        RunAsync(me => _friends.UnblockAsync(me, row.UserId), _ => $"{row.Username} was unblocked.");

    internal void OpenReport(int otherId, string username) =>
        Report.Open(otherId, username, async notice =>
        {
            Error = null;
            Notice = notice;
            if (UserId is { } me) await ReloadAsync(me);
        });

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
        await ReloadAsync(me);
    }

    private async Task ReloadAsync(int me)
    {
        await LoadAsync();
        if (Results.Count > 0) await RefreshResultsAsync(me);
        try { await _inbox.RefreshUnreadAsync(me); }   // accepting from the search clears an inbox request
        catch (Exception) { }
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

    /// <summary>The phones' Leaderboard / Map switch.</summary>
    [RelayCommand] private void OpenMap() => _shell().ShowGyms();
}
