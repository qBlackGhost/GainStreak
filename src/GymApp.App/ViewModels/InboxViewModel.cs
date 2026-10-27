using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Services;
using GymApp.Social;

namespace GymApp.App.ViewModels;

/// <summary>"2 h ago", "Yesterday", "Mon", "14 Oct" for an inbox row.</summary>
public static class InboxTime
{
    public static string Ago(DateTime utc, DateTime nowLocal)
    {
        var at = utc.ToLocalTime();
        var since = nowLocal - at;
        if (since < TimeSpan.FromMinutes(1)) return "Just now";
        if (since < TimeSpan.FromHours(1)) return $"{(int)since.TotalMinutes} min ago";
        if (at.Date == nowLocal.Date) return $"{(int)since.TotalHours} h ago";
        if (at.Date == nowLocal.Date.AddDays(-1)) return "Yesterday";
        if (at.Date > nowLocal.Date.AddDays(-7)) return at.ToString("ddd", CultureInfo.InvariantCulture);
        return at.ToString("d MMM", CultureInfo.InvariantCulture);
    }
}

/// <summary>A row under "New". Each item kind has its own row view model and data template (InboxView.axaml).</summary>
public abstract class InboxRowViewModel(InboxItem item, DateTime nowLocal) : ObservableObject
{
    public InboxItem Item => item;
    public string Username => item.FromUsername;
    public string Initials => ViewModels.Initials.Of(item.FromUsername);
    public bool IsNew => item.IsNew;
    public string WhenText => InboxTime.Ago(item.At, nowLocal);
}

public partial class FriendRequestRowViewModel(FriendRequestItem item, DateTime nowLocal, InboxViewModel owner)
    : InboxRowViewModel(item, nowLocal)
{
    public string StatusText => $"Wants to be friends · {WhenText}";

    [RelayCommand] private Task Accept() => owner.AcceptAsync(item);
    [RelayCommand] private Task Decline() => owner.DeclineAsync(item);
}

/// <summary>A line under "Earlier".</summary>
public record InboxHistoryRowViewModel(InboxHistoryEntry Entry, DateTime NowLocal)
{
    public string Text => Entry.Text;
    public string WhenText => InboxTime.Ago(Entry.At, NowLocal);
}

/// <summary>The inbox: friend requests, and the splits and workouts friends share, filtered by the pill (All / Requests /
/// Workouts). Accept and Decline go straight to the friend service; adding a share opens its popup (<see cref="SplitAdd"/>,
/// <see cref="WorkoutAdd"/>), applies it on this phone and then removes it from the server.</summary>
public partial class InboxViewModel : ViewModelBase, IClosesOnBack
{
    /// <summary>Back closes the add-a-shared-split or add-a-shared-workout popup first.</summary>
    public bool CloseOnBack()
    {
        if (SplitAdd.IsOpen) { SplitAdd.IsOpen = false; return true; }
        if (WorkoutAdd.IsOpen) { WorkoutAdd.IsOpen = false; return true; }
        return false;
    }

    public const int AllFilter = 0, RequestsFilter = 1, WorkoutsFilter = 2;

    private readonly InboxService _inbox;
    private readonly IFriendService _friends;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;
    private readonly IShareService _shares;
    private readonly SplitShareService _splitShares;
    private readonly WorkoutMenuService _menu;
    private readonly Func<DateTime> _now;
    private InboxView? _view;

    public InboxViewModel(InboxService inbox, IFriendService friends, SessionService session, Func<MainViewModel> shell,
                          IShareService shares, SplitShareService splitShares, WorkoutMenuService menu,
                          Func<DateTime>? now = null)
    {
        _inbox = inbox;
        _friends = friends;
        _session = session;
        _shell = shell;
        _shares = shares;
        _splitShares = splitShares;
        _menu = menu;
        _now = now ?? (() => DateTime.Now);
    }

    public SplitAddViewModel SplitAdd { get; } = new();
    public WorkoutAddViewModel WorkoutAdd { get; } = new();

    /// <summary>Call right after resolving from DI.</summary>
    public Task LoadAsync() => ReloadAsync();

    public ObservableCollection<InboxRowViewModel> Rows { get; } = new();
    public ObservableCollection<InboxHistoryRowViewModel> History { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAll), nameof(IsRequests), nameof(IsWorkouts))]
    public partial int FilterIndex { get; set; }

    public bool IsAll => FilterIndex == AllFilter;
    public bool IsRequests => FilterIndex == RequestsFilter;
    public bool IsWorkouts => FilterIndex == WorkoutsFilter;

    [ObservableProperty] public partial string Subtitle { get; set; } = "";
    [ObservableProperty] public partial string RequestsLabel { get; set; } = "Requests";
    [ObservableProperty] public partial string WorkoutsLabel { get; set; } = "Workouts";
    [ObservableProperty] public partial bool HasRows { get; set; }
    [ObservableProperty] public partial bool IsCaughtUp { get; set; }
    [ObservableProperty] public partial string EmptyText { get; set; } = "";
    [ObservableProperty] public partial bool HasHistory { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial string? StaleNotice { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MarkAllReadCommand))]
    public partial bool HasUnread { get; set; }

    private int? UserId => _session.CurrentUser?.Id;

    private async Task ReloadAsync()
    {
        if (UserId is not { } me) return;
        try
        {
            _view = await _inbox.GetAsync(me);
            StaleNotice = StaleText.For(_view);
        }
        catch (Exception)
        {
            Error = "Could not load your inbox.";
        }
        Show();
    }

    /// <summary>Rebuilds the lists from the last load for the current filter.</summary>
    private void Show()
    {
        var items = _view?.Items ?? [];
        var history = _view?.History ?? [];
        var now = _now();
        bool Shows(InboxKind kind) => FilterIndex switch
        {
            RequestsFilter => kind == InboxKind.FriendRequest,
            WorkoutsFilter => kind is InboxKind.SharedSplit or InboxKind.SharedWorkout,
            _ => true
        };

        Rows.Clear();
        foreach (var i in items.Where(i => Shows(i.Kind)))
            Rows.Add(i switch
            {
                FriendRequestItem r => new FriendRequestRowViewModel(r, now, this),
                SharedSplitItem s => new SharedSplitRowViewModel(s, now, this),
                SharedWorkoutItem w => (InboxRowViewModel)new SharedWorkoutRowViewModel(w, now, this),
                _ => throw new InvalidOperationException($"No row for {i.Kind}")
            });
        History.Clear();
        foreach (var h in history.Where(h => Shows(h.Kind))) History.Add(new InboxHistoryRowViewModel(h, now));

        HasRows = Rows.Count > 0;
        IsCaughtUp = !HasRows;
        HasHistory = History.Count > 0;

        var waiting = items.Count;
        var fresh = items.Count(i => i.IsNew);
        HasUnread = fresh > 0;
        Subtitle = waiting == 0 ? "Nothing waiting"
            : (waiting == 1 ? "1 thing waiting" : $"{waiting} things waiting") + (fresh > 0 ? $" · {fresh} new" : "");

        var requests = items.Count(i => i.Kind == InboxKind.FriendRequest);
        RequestsLabel = requests > 0 ? $"Requests {requests}" : "Requests";
        var workouts = items.Count(i => i.Kind is InboxKind.SharedSplit or InboxKind.SharedWorkout);
        WorkoutsLabel = workouts > 0 ? $"Workouts {workouts}" : "Workouts";
        EmptyText = FilterIndex switch
        {
            RequestsFilter => "Friend requests show up here.",
            WorkoutsFilter => "Splits and workouts your friends share with you show up here.",
            _ => "Friend requests, and splits and workouts your friends share, show up here."
        };
    }

    partial void OnFilterIndexChanged(int value) => Show();

    [RelayCommand] private void ShowAll() => FilterIndex = AllFilter;
    [RelayCommand] private void ShowRequests() => FilterIndex = RequestsFilter;
    [RelayCommand] private void ShowWorkouts() => FilterIndex = WorkoutsFilter;

    [RelayCommand(CanExecute = nameof(HasUnread))]
    private void MarkAllRead()
    {
        if (UserId is not { } me || _view is null) return;
        _inbox.MarkAllRead(me, _view);
        _view = _view with { Items = _view.Items.Select(i => i with { IsNew = false }).ToList() };
        Show();
    }

    internal async Task AcceptAsync(FriendRequestItem item)
    {
        if (UserId is not { } me) return;
        try
        {
            var r = await _friends.AcceptAsync(me, item.FromUserId);
            Error = r.Success ? null : r.Error;
            if (r.Success) _inbox.Record(me, InboxKind.FriendRequest, $"You're now friends with {item.FromUsername}");
        }
        catch (Exception)
        {
            Error = "Something went wrong. Try again.";
        }
        await ReloadAsync();
    }

    // Declining leaves no line under Earlier: the other person isn't told either
    internal async Task DeclineAsync(FriendRequestItem item)
    {
        if (UserId is not { } me) return;
        try
        {
            var r = await _friends.DeclineAsync(me, item.FromUserId);
            Error = r.Success ? null : r.Error;
        }
        catch (Exception)
        {
            Error = "Something went wrong. Try again.";
        }
        await ReloadAsync();
    }

    // ===== Shares =====

    internal void OpenSplitAdd(SharedSplitRowViewModel row) =>
        SplitAdd.Open(row, _now().DayOfWeek, async (r, start, mode) =>
        {
            if (UserId is not { } me) return "You're signed out.";
            try
            {
                var result = await _splitShares.ImportSplitAsync(me, r.Share.Split, start, mode);
                if (!result.Success) return result.Error;
                _inbox.Record(me, InboxKind.SharedSplit, $"You started {r.Share.Split.Name} from {r.Username}");
                await FinishShareAsync(r.Share.ShareId);
                return null;
            }
            catch (Exception)
            {
                return "Could not add that split.";
            }
        });

    internal async Task OpenWorkoutAddAsync(SharedWorkoutRowViewModel row)
    {
        if (UserId is not { } me) return;
        WorkoutMenu? menu = null;
        try { menu = await _menu.GetAsync(me); }
        catch (Exception) { Error = "Could not load your split."; }
        WorkoutAdd.Open(row, menu, async (r, day, mode) =>
        {
            try
            {
                var result = await _splitShares.ImportWorkoutAsync(me, r.Share.Workout, day.SplitDayId, mode);
                if (!result.Success) return result.Error;
                _inbox.Record(me, InboxKind.SharedWorkout, $"You loaded {r.Share.Workout.DayName} from {r.Username} into {day.Name}");
                await FinishShareAsync(r.Share.ShareId);
                return null;
            }
            catch (Exception)
            {
                return "Could not load that workout.";
            }
        });
    }

    /// <summary>Added on this phone: the server copy can go. If that fails it shows again next time, which is harmless.</summary>
    private async Task FinishShareAsync(long shareId)
    {
        try { await _shares.RemoveAsync(shareId); }
        catch (Exception) { }
        await ReloadAsync();
    }

    // Dismissing leaves no line under Earlier
    internal async Task DismissAsync(long shareId)
    {
        try { Error = await _shares.RemoveAsync(shareId); }
        catch (Exception) { Error = "Something went wrong. Try again."; }
        await ReloadAsync();
    }

    [RelayCommand] private void GoToSplits()
    {
        WorkoutAdd.IsOpen = false;
        _shell().ShowSplits();
    }

    [RelayCommand] private void FindFriends() => _shell().ShowFriends();
}
