using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Contracts;
using GymApp.Services;
using GymApp.Social;

namespace GymApp.App.ViewModels;

/// <summary>What the share popup sends: a whole split (Splits screen) or one workout (Workouts screen).</summary>
public enum ShareMode { Split, Workout }

/// <summary>A split or a day to pick in the share popup. One without exercises can't be shared.</summary>
public partial class ShareChoiceViewModel(int id, string title, string detail, bool canShare, ShareDialogViewModel owner)
    : ObservableObject
{
    public int Id => id;
    public string Title => title;
    public string Detail => detail;
    public bool CanShare => canShare;

    [ObservableProperty] public partial bool IsSelected { get; set; }

    [RelayCommand(CanExecute = nameof(CanShare))] private void Choose() => owner.Choose(this);
}

public partial class ShareFriendViewModel(Friend friend) : ObservableObject
{
    public int UserId => friend.UserId;
    public string Username => friend.Username;
    public string Initials => ViewModels.Initials.Of(friend.Username);

    [ObservableProperty] public partial bool IsChecked { get; set; }
}

/// <summary>The share popup over Workouts and Splits: pick what to share, tick friends, send. Tells the sender what the
/// friend will get, in the same words the friend sees.</summary>
public partial class ShareDialogViewModel : ObservableObject
{
    private readonly IFriendService _friends;
    private readonly IShareService _shares;
    private readonly SplitShareService _splitShares;
    private readonly WorkoutMenuService _menu;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;
    private Action<string>? _done;

    public ShareDialogViewModel(IFriendService friends, IShareService shares, SplitShareService splitShares,
                                WorkoutMenuService menu, SessionService session, Func<MainViewModel> shell)
    {
        _friends = friends; _shares = shares; _splitShares = splitShares; _menu = menu; _session = session; _shell = shell;
    }

    public ObservableCollection<ShareChoiceViewModel> Choices { get; } = new();
    public ObservableCollection<ShareFriendViewModel> Friends { get; } = new();

    [ObservableProperty] public partial bool IsOpen { get; set; }
    [ObservableProperty] public partial ShareMode Mode { get; set; }
    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string ChoiceLabel { get; set; } = "";
    [ObservableProperty] public partial string Explanation { get; set; } = "";
    [ObservableProperty] public partial bool HasNoChoices { get; set; }
    [ObservableProperty] public partial string NoChoicesText { get; set; } = "";
    [ObservableProperty] public partial bool HasNoFriends { get; set; }
    [ObservableProperty] public partial bool IsSending { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial ShareChoiceViewModel? Chosen { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial int CheckedCount { get; set; }

    public string SendLabel => CheckedCount <= 1 ? "Share" : $"Share with {CheckedCount}";

    partial void OnCheckedCountChanged(int value) => OnPropertyChanged(nameof(SendLabel));

    private int? UserId => _session.CurrentUser?.Id;

    /// <param name="done">Called with the "Shared with …" line after a send.</param>
    public async Task OpenAsync(ShareMode mode, Action<string> done)
    {
        _done = done;
        Mode = mode;
        Error = null;
        Chosen = null;
        Choices.Clear();
        Friends.Clear();
        CheckedCount = 0;
        Title = mode == ShareMode.Split ? "Share a split" : "Share a workout";
        ChoiceLabel = mode == ShareMode.Split ? "SPLIT" : "WORKOUT";
        Explanation = mode == ShareMode.Split
            ? "Your friends get the whole split: every day and the exercises in it, with their planned sets. They're told the same before they add it."
            : "Your friends get this day's exercises and planned sets. They choose which day of their own split to load them into.";
        NoChoicesText = mode == ShareMode.Split
            ? "Add exercises to a split's days first."
            : "Add exercises to one of your days first.";
        IsOpen = true;
        if (UserId is not { } me) return;

        try
        {
            int? todayId = null;
            if (mode == ShareMode.Split)
                foreach (var s in await _splitShares.GetShareableSplitsAsync(me))
                    Choices.Add(new ShareChoiceViewModel(s.SplitId, s.Name,
                        $"{Plural(s.DayCount, "day")} · {Plural(s.ExerciseCount, "exercise")}" + (s.IsActive ? " · active" : ""),
                        s.ExerciseCount > 0, this));
            else if (await _menu.GetAsync(me) is { } menu)
            {
                foreach (var d in menu.Days)
                    Choices.Add(new ShareChoiceViewModel(d.SplitDayId, d.Name,
                        $"{menu.SplitName} · {Plural(d.ExerciseCount, "exercise")}", d.ExerciseCount > 0, this));
                todayId = menu.Days.FirstOrDefault(d => d.IsToday)?.SplitDayId;
            }

            // Today's workout (the active split comes first on Splits), else the first that has exercises
            var first = Choices.FirstOrDefault(c => c.CanShare && c.Id == todayId) ?? Choices.FirstOrDefault(c => c.CanShare);
            if (first is not null) Choose(first);
            HasNoChoices = !Choices.Any(c => c.CanShare);

            foreach (var f in await _friends.GetFriendsAsync(me))
            {
                var row = new ShareFriendViewModel(f);
                row.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ShareFriendViewModel.IsChecked)) CheckedCount = Friends.Count(x => x.IsChecked);
                };
                Friends.Add(row);
            }
            HasNoFriends = Friends.Count == 0;
        }
        catch (Exception)
        {
            Error = "Could not load what to share.";
        }
    }

    private static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

    internal void Choose(ShareChoiceViewModel choice)
    {
        Chosen = choice;
        foreach (var c in Choices) c.IsSelected = c == choice;
    }

    private bool CanSend() => Chosen is not null && CheckedCount > 0 && !IsSending;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (UserId is not { } me || Chosen is not { } chosen) return;
        IsSending = true;
        SendCommand.NotifyCanExecuteChanged();
        try
        {
            var to = Friends.Where(f => f.IsChecked).ToList();
            ShareRequest? request = Mode == ShareMode.Split
                ? await _splitShares.BuildSplitAsync(me, chosen.Id) is { } split ? new(to.Select(f => f.UserId).ToList(), split, null) : null
                : await _splitShares.BuildWorkoutAsync(me, chosen.Id) is { } workout ? new(to.Select(f => f.UserId).ToList(), null, workout) : null;
            if (request is null) { Error = "That's no longer there. Close and try again."; return; }

            var r = await _shares.SendAsync(request);
            if (!r.Success) { Error = r.Error; return; }

            IsOpen = false;
            var names = to.Count == 1 ? to[0].Username : $"{to.Count} friends";
            _done?.Invoke($"Shared {chosen.Title} with {names}.");
        }
        catch (Exception)
        {
            Error = "Something went wrong. Try again.";
        }
        finally
        {
            IsSending = false;
            SendCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand] private void Close() => IsOpen = false;

    [RelayCommand]
    private void FindFriends()
    {
        IsOpen = false;
        _shell().ShowFriends();
    }
}
