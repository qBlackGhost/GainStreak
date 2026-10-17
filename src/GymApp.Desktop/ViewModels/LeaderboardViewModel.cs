using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Core;
using GymApp.Services;

namespace GymApp.Desktop.ViewModels;

/// <summary>One row of the board, formatted. Ranking comes from <see cref="Leaderboard"/>; nothing is computed here.</summary>
public record LeaderboardRowViewModel(LeaderboardRow Row)
{
    public string RankText => $"#{Row.Rank}";
    public string Username => Row.IsYou ? $"{Row.Username} (you)" : Row.Username;
    public string LevelText => $"Level {Row.Level.Number} · {Row.Level.Name}";
    public string PointsText => $"{Row.Points.ToString("N0", CultureInfo.InvariantCulture)} pts";
    public string StreakText => $"🔥 {Row.CurrentStreak}";
    public bool IsYou => Row.IsYou;
}

/// <summary>You and your friends, ranked by this week's or all-time points, with each person's streak.</summary>
public partial class LeaderboardViewModel : ViewModelBase
{
    private readonly LeaderboardService _board;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;
    private bool _backToFriends;

    public LeaderboardViewModel(LeaderboardService board, SessionService session, Func<MainViewModel> shell)
    {
        _board = board;
        _session = session;
        _shell = shell;
        _ = LoadAsync();
    }

    public void OpenedFromFriends() => _backToFriends = true;

    public ObservableCollection<LeaderboardRowViewModel> Rows { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllTime))]
    public partial bool IsThisWeek { get; set; } = true;
    public bool IsAllTime
    {
        get => !IsThisWeek;
        set => IsThisWeek = !value;
    }

    [ObservableProperty] public partial string WeekText { get; set; } = "";
    [ObservableProperty] public partial bool IsAlone { get; set; }
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }

    partial void OnIsThisWeekChanged(bool value) => _ = LoadAsync();

    private async Task LoadAsync()
    {
        if (_session.CurrentUser is not { } user) return;
        IsLoading = true;
        try
        {
            var period = IsThisWeek ? LeaderboardPeriod.ThisWeek : LeaderboardPeriod.AllTime;
            var view = await _board.GetAsync(user.Id, period);
            if (view is null) return;
            if (view.Period != (IsThisWeek ? LeaderboardPeriod.ThisWeek : LeaderboardPeriod.AllTime)) return;   // toggled meanwhile

            Rows.Clear();
            foreach (var r in view.Rows) Rows.Add(new LeaderboardRowViewModel(r));
            WeekText = IsThisWeek ? Leaderboard.WeekTitle(view.WeekStart) : "All-time points";
            IsAlone = view.IsAlone;
            Error = null;
        }
        catch (Exception)
        {
            Error = "Could not load the leaderboard.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ShowFriends() => _shell().ShowFriends();

    [RelayCommand]
    private void Back()
    {
        if (_backToFriends) _shell().ShowFriends();
        else _shell().ShowHome();
    }
}
