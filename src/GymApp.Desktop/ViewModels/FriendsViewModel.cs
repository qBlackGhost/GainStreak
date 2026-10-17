using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Services;

namespace GymApp.Desktop.ViewModels;

/// <summary>One accepted friend. Remove asks for confirmation first.</summary>
public partial class FriendRowViewModel(Friend friend, FriendsViewModel owner) : ObservableObject
{
    public int UserId => friend.UserId;
    public string Username => friend.Username;
    public string SinceText => $"Friends since {friend.Since.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture)}";
    public string ConfirmText => $"Remove {friend.Username} from your friends? You'll leave each other's leaderboard.";

    [ObservableProperty] public partial bool IsConfirmingRemove { get; set; }

    [RelayCommand] private void Remove() => IsConfirmingRemove = true;
    [RelayCommand] private void CancelRemove() => IsConfirmingRemove = false;
    [RelayCommand] private Task ConfirmRemove() => owner.RemoveAsync(this);
}

/// <summary>A pending request: incoming ones can be accepted or declined, outgoing ones cancelled.</summary>
public partial class RequestRowViewModel(FriendRequest request, bool incoming, FriendsViewModel owner) : ObservableObject
{
    public int UserId => request.UserId;
    public string Username => request.Username;
    public bool IsIncoming => incoming;
    public bool IsOutgoing => !incoming;
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

/// <summary>Friends, pending requests and the username search. Only reads results from FriendService.</summary>
public partial class FriendsViewModel : ViewModelBase
{
    private readonly FriendService _friends;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;

    public FriendsViewModel(FriendService friends, SessionService session, Func<MainViewModel> shell)
    {
        _friends = friends;
        _session = session;
        _shell = shell;
        _ = LoadAsync();
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

    private int? UserId => _session.CurrentUser?.Id;

    private async Task LoadAsync()
    {
        if (UserId is not { } me) return;
        try
        {
            var friends = await _friends.GetFriendsAsync(me);
            Friends.Clear();
            foreach (var f in friends) Friends.Add(new FriendRowViewModel(f, this));
            FriendsTitle = friends.Count == 0 ? "Friends" : $"Friends ({friends.Count}/{FriendService.MaxFriends})";
            HasNoFriends = friends.Count == 0;

            var requests = await _friends.GetRequestsAsync(me);
            Incoming.Clear();
            foreach (var r in requests.Incoming) Incoming.Add(new RequestRowViewModel(r, true, this));
            Outgoing.Clear();
            foreach (var r in requests.Outgoing) Outgoing.Add(new RequestRowViewModel(r, false, this));
            HasIncoming = Incoming.Count > 0;
            HasOutgoing = Outgoing.Count > 0;
            HasNoRequests = !HasIncoming && !HasOutgoing;
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
        if (UserId is not { } me) return;
        Notice = null;
        var r = await _friends.SearchAsync(me, Query);
        Results.Clear();
        if (!r.Success) { SearchHint = r.Error; return; }

        foreach (var u in r.Users) Results.Add(new FoundUserViewModel(u, this));
        SearchHint = r.Users.Count == 0 ? $"No one found starting with \"{Query.Trim()}\"." : null;
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
        var r = await _friends.SearchAsync(me, Query);
        if (!r.Success) return;
        Results.Clear();
        foreach (var u in r.Users) Results.Add(new FoundUserViewModel(u, this));
    }

    [RelayCommand]
    private void ShowLeaderboard() => _shell().ShowLeaderboard(fromFriends: true);

    [RelayCommand]
    private void Back() => _shell().ShowHome();
}
