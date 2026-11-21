using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using GymApp.App;
using GymApp.App.ViewModels;
using GymApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GymApp.TestBench;

public record UserItem(int Id, string Username)
{
    public override string ToString() => Username;
}

public record GymItem(int Id, string Name, double Latitude, double Longitude)
{
    public override string ToString() => Name;
}

/// <summary>
/// The admin window. Every action works on the signed-in user, then reloads Home so the app shows the result
/// (Home recalculates the streak and syncs points when it loads, like after a real overnight change).
/// </summary>
public partial class AdminViewModel : ObservableObject
{
    private readonly BenchSettings _settings;
    private readonly SessionService _session;
    private readonly MainViewModel _main;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly StreakService _streaks;
    private readonly PointsService _points;
    private readonly WorkoutService _workouts;
    private readonly VacationService _vacations;

    public AdminViewModel(BenchSettings settings)
    {
        _settings = settings;
        var sp = GymApplication.Services;
        _session = sp.GetRequiredService<SessionService>();
        _main = sp.GetRequiredService<MainViewModel>();
        _dbFactory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        _streaks = sp.GetRequiredService<StreakService>();
        _points = sp.GetRequiredService<PointsService>();
        _workouts = sp.GetRequiredService<WorkoutService>();
        _vacations = sp.GetRequiredService<VacationService>();

        _session.Changed += () => _ = RefreshStatusAsync();
        _session.Changed += () => _ = LoadGymsAsync();
        _ = LoadUsersAsync();
        _ = LoadGymsAsync();
        _ = RefreshStatusAsync();
    }

    public string DataDir => BenchSettings.DataDir;

    // ===== Status =====
    [ObservableProperty] public partial string ClockText { get; set; } = "";
    [ObservableProperty] public partial string OffsetText { get; set; } = "";
    [ObservableProperty] public partial string UserText { get; set; } = "";
    [ObservableProperty] public partial string PointsText { get; set; } = "";
    [ObservableProperty] public partial string StreakText { get; set; } = "";
    [ObservableProperty] public partial string Message { get; set; } = "";
    [ObservableProperty] public partial bool IsSignedIn { get; set; }

    // ===== Clock =====
    [ObservableProperty] public partial DateTime? PickedDate { get; set; }

    partial void OnPickedDateChanged(DateTime? value)
    {
        if (value is { } d && DateOnly.FromDateTime(d) != AppClock.Today)
            _ = SetDateAsync(DateOnly.FromDateTime(d));
    }

    [RelayCommand] private Task PrevDay() => SetDateAsync(AppClock.Today.AddDays(-1));
    [RelayCommand] private Task NextDay() => SetDateAsync(AppClock.Today.AddDays(1));
    [RelayCommand] private Task NextWeek() => SetDateAsync(AppClock.Today.AddDays(7));
    [RelayCommand] private Task RealToday() => SetDateAsync(DateOnly.FromDateTime(DateTime.Now));

    private Task SetDateAsync(DateOnly date) => RunAsync(() =>
    {
        _settings.DayOffset = date.DayNumber - DateOnly.FromDateTime(DateTime.Now).DayNumber;
        AppClock.Offset = TimeSpan.FromDays(_settings.DayOffset);
        _settings.Save();
        return Task.FromResult($"Clock set to {date:ddd d MMM yyyy}.");
    });

    // ===== Account =====
    public ObservableCollection<UserItem> Users { get; } = [];
    [ObservableProperty] public partial UserItem? SelectedUser { get; set; }

    // ===== Social mode =====
    public bool IsServerSocial => _settings.ServerSocial;
    public bool IsLocalSocial => !_settings.ServerSocial;
    public string SocialModeText => _settings.ServerSocial
        ? "Server: the app's own wiring. Sign in with a real account; the clock must stay within 2 days of today."
        : "Local: accounts and friends on this computer only. The clock moves freely.";
    public string SwitchSocialLabel => _settings.ServerSocial ? "Switch to Local" : "Switch to Server";

    [RelayCommand]
    private void SwitchSocial() => Restart(() => _settings.ServerSocial = !_settings.ServerSocial);

    // ===== Location (18c): what the live workout's location reads =====
    public ObservableCollection<GymItem> Gyms { get; } = [];
    [ObservableProperty] public partial GymItem? SelectedGym { get; set; }
    [ObservableProperty] public partial string LocationText { get; set; } = "Away: nowhere near a gym.";

    [RelayCommand] private Task ReloadGyms() => LoadGymsAsync();

    /// <summary>The signed-in user's gyms that have a position: the ones a workout can be verified at.</summary>
    private async Task LoadGymsAsync()
    {
        var userId = _session.CurrentUser?.Id;
        await using var db = await _dbFactory.CreateDbContextAsync();
        var gyms = await db.Gyms.AsNoTracking()
            .Where(g => g.IsActive && g.Latitude != null && g.Longitude != null && (g.OwnerUserId == null || g.OwnerUserId == userId))
            .OrderBy(g => g.Name)
            .Select(g => new GymItem(g.Id, g.Name, g.Latitude!.Value, g.Longitude!.Value))
            .ToListAsync();
        Gyms.Clear();
        foreach (var g in gyms) Gyms.Add(g);
        SelectedGym ??= Gyms.FirstOrDefault();
    }

    [RelayCommand]
    private void AtGym()
    {
        if (SelectedGym is not { } gym) { LocationText = "Pick a gym first (load a city's gyms in the app)."; return; }
        Program.Location.SetAtGym(gym.Latitude, gym.Longitude);
        LocationText = $"At {gym.Name}.";
    }

    [RelayCommand]
    private void Away()
    {
        Program.Location.SetAway();
        LocationText = "Away: nowhere near a gym.";
    }

    [RelayCommand]
    private void InAnotherCity()
    {
        Program.Location.SetInAnotherCity();
        LocationText = "In another city: Rome, far from home.";
    }

    /// <summary>Home looks for a trip at most every 6 hours (19c): forget the last look and reload Home, so it looks now.</summary>
    [RelayCommand]
    private Task LookForTrip() => RunAsync(async () =>
    {
        if (_session.CurrentUser is not { } user) return "Sign in first.";
        await using (var db = await _dbFactory.CreateDbContextAsync())
            await db.UserProfiles.Where(p => p.UserId == user.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(p => p.LastAwayCheckAt, (DateTime?)null));
        GymApplication.Services.GetRequiredService<AwayWatcher>().Clear(user.Id);
        return "Home looks for a trip now (if location is allowed and there's a streak and a home city).";
    });

    [RelayCommand]
    private void Approximate()
    {
        Program.Location.SetApproximate();
        LocationText = "Approximate: precise location is off.";
    }

    [RelayCommand]
    private void NoPermission()
    {
        Program.Location.SetNoPermission();
        LocationText = "No permission: location is off.";
    }

    // ===== Logging: the desktop's check-in or the phones' live workout =====
    public string WorkoutModeText => _settings.PhoneWorkout
        ? "Phone: Home runs the live workout, one set at a time, like the phones."
        : "Desktop: Home has the \"I went today\" check-in.";
    public string SwitchWorkoutLabel => _settings.PhoneWorkout ? "Switch to the check-in" : "Switch to the live workout";

    [RelayCommand]
    private void SwitchWorkout() => Restart(() => _settings.PhoneWorkout = !_settings.PhoneWorkout);

    [RelayCommand(CanExecute = nameof(IsLocalSocial))]
    private Task SignInAs() => RunAsync(async () =>
    {
        if (SelectedUser is not { } pick) return "Pick a user first.";
        await using var db = await _dbFactory.CreateDbContextAsync();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == pick.Id);
        if (user is null) return "That user no longer exists.";
        _session.SignIn(user);   // no password; also navigates to Home
        return $"Signed in as {user.Username}.";
    }, reloadHome: false);

    [RelayCommand]
    private Task SignOut() => RunAsync(async () =>
    {
        // Through the account service, so Server mode also forgets the token and the cached friends
        await GymApplication.Services.GetRequiredService<IAccountService>().LogOutAsync();
        return "Signed out.";
    }, reloadHome: false);

    [RelayCommand] private Task ReloadUsers() => LoadUsersAsync();

    private async Task LoadUsersAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var users = await db.Users.AsNoTracking().OrderBy(u => u.Username).Select(u => new UserItem(u.Id, u.Username)).ToListAsync();
        Users.Clear();
        foreach (var u in users) Users.Add(u);
        SelectedUser = Users.FirstOrDefault(u => u.Id == _session.CurrentUser?.Id) ?? Users.FirstOrDefault();
    }

    // ===== Workouts =====

    /// <summary>Logs today's planned day without the sets rule. On a rest day it takes the first day with exercises,
    /// like the real rest-day picker's default.</summary>
    [RelayCommand] private Task QuickCheckIn() => RunAsync(CheckInAsync);

    [RelayCommand]
    private Task CheckInAndNextDay() => RunAsync(async () =>
    {
        var result = await CheckInAsync();
        _settings.DayOffset++;
        AppClock.Offset = TimeSpan.FromDays(_settings.DayOffset);
        _settings.Save();
        return $"{result} Moved to {AppClock.Today:ddd d MMM}.";
    });

    private async Task<string> CheckInAsync()
    {
        if (_session.CurrentUser is not { } user) return "Sign in first.";
        var date = AppClock.Today;
        if ((await _streaks.GetAsync(user.Id, date)).Status == StreakStatus.NeedsChoice)
            return "Answer the missed-day card on Home first.";

        var info = await _workouts.GetCheckInAsync(user.Id, today: date);
        if (!info.HasSplit) return "Pick a split first.";
        if (info.Logged) return $"Already checked in on {date:ddd d MMM}.";
        if (info.Day is not { } day) return "No split day has exercises yet; add some first.";

        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.WorkoutLogs.Add(new WorkoutLog { UserId = user.Id, Date = date, SplitDayId = day.SplitDayId });
            await db.SaveChangesAsync();
        }
        await _streaks.RecalculateAsync(user.Id, date);
        var sync = await _points.SyncAsync(user.Id, date);
        return $"Checked in {day.Name} on {date:ddd d MMM} (points {sync?.OldTotal} → {sync?.NewTotal}).";
    }

    [RelayCommand]
    private Task UndoCheckIn() => RunAsync(async () =>
    {
        if (_session.CurrentUser is not { } user) return "Sign in first.";
        var result = await _workouts.UndoTodayAsync(user.Id, AppClock.Today);
        return $"Undid {AppClock.Today:ddd d MMM}'s check-in (points {result.Points?.OldTotal} → {result.Points?.NewTotal}).";
    });

    /// <summary>Forgets the stored missed-day choices from yesterday on, so the card asks again. A "Shift my whole
    /// schedule" already moved the schedule; that part stays.</summary>
    [RelayCommand]
    private Task ForgetMissedChoices() => RunAsync(async () =>
    {
        if (_session.CurrentUser is not { } user) return "Sign in first.";
        var from = AppClock.Today.AddDays(-1);
        int deleted;
        await using (var db = await _dbFactory.CreateDbContextAsync())
            deleted = await db.MissedDayChoices.Where(c => c.UserId == user.Id && c.Date >= from).ExecuteDeleteAsync();
        await _streaks.RecalculateAsync(user.Id, AppClock.Today);
        return $"Forgot {deleted} missed-day choice(s).";
    });

    // ===== Vacation (0.19.0) =====
    [ObservableProperty] public partial decimal? VacationDays { get; set; } = 3;

    /// <summary>Starts a vacation now (today, or tomorrow if today is logged), the given number of days long. The
    /// cooldown applies: clear it first to start another.</summary>
    [RelayCommand]
    private Task StartVacation() => RunAsync(async () =>
    {
        if (_session.CurrentUser is not { } user) return "Sign in first.";
        var days = (int)Math.Clamp(VacationDays ?? 3, 1, VacationRules.MaxDays);
        var start = await _vacations.StartDateAsync(user.Id);
        var r = await _vacations.StartAsync(user.Id, start.AddDays(days));
        return r.Success ? $"On vacation {start:ddd d MMM}, back on {start.AddDays(days):ddd d MMM}." : $"Refused: {r.Message}";
    });

    [RelayCommand]
    private Task EndVacation() => RunAsync(async () =>
    {
        if (_session.CurrentUser is not { } user) return "Sign in first.";
        var r = await _vacations.EndEarlyAsync(user.Id);
        return r.Success ? "Vacation ended (or cancelled before it started)." : $"Refused: {r.Message}";
    });

    /// <summary>Deletes every vacation, so the 60 days don't hold the next one back. Frozen days go with them.</summary>
    [RelayCommand]
    private Task ClearVacations() => RunAsync(async () =>
    {
        if (_session.CurrentUser is not { } user) return "Sign in first.";
        int deleted;
        await using (var db = await _dbFactory.CreateDbContextAsync())
            deleted = await db.Vacations.Where(v => v.UserId == user.Id).ExecuteDeleteAsync();
        await _streaks.RecalculateAsync(user.Id, AppClock.Today);
        return $"Deleted {deleted} vacation(s); the cooldown is clear.";
    });

    // ===== Points =====
    [ObservableProperty] public partial decimal? PointsAmount { get; set; } = 100;

    [RelayCommand]
    private Task AddPoints() => RunAsync(async () =>
    {
        if (_session.CurrentUser is not { } user) return "Sign in first.";
        var amount = (int)(PointsAmount ?? 0);
        if (amount == 0) return "Enter an amount (negative takes points away).";
        return await AdjustAsync(user.Id, amount);
    });

    [RelayCommand]
    private Task SetPointsTotal() => RunAsync(async () =>
    {
        if (_session.CurrentUser is not { } user) return "Sign in first.";
        await using var db = await _dbFactory.CreateDbContextAsync();
        var current = await db.Users.Where(u => u.Id == user.Id).Select(u => u.Points).FirstAsync();
        var diff = (int)(PointsAmount ?? 0) - current;
        return diff == 0 ? "Already at that total." : await AdjustAsync(user.Id, diff);
    });

    private async Task<string> AdjustAsync(int userId, int amount)
    {
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.PointsEntries.Add(new PointsEntry
            {
                UserId = userId, Reason = PointsReason.Adjustment, Amount = amount, Date = AppClock.Today,
                DedupKey = $"adjust:{Guid.NewGuid():N}"
            });
            await db.SaveChangesAsync();
        }
        var sync = await _points.SyncAsync(userId, AppClock.Today);   // also refreshes the cached total
        return $"Points {sync?.OldTotal} → {sync?.NewTotal}.";
    }

    [RelayCommand]
    private Task ClearAdjustments() => RunAsync(async () =>
    {
        if (_session.CurrentUser is not { } user) return "Sign in first.";
        int deleted;
        await using (var db = await _dbFactory.CreateDbContextAsync())
            deleted = await db.PointsEntries.Where(p => p.UserId == user.Id && p.Reason == PointsReason.Adjustment)
                .ExecuteDeleteAsync();
        var sync = await _points.SyncAsync(user.Id, AppClock.Today);
        return $"Removed {deleted} manual adjustment(s) (points {sync?.OldTotal} → {sync?.NewTotal}).";
    });

    /// <summary>Recomputes the whole ledger from the first log up to the clock date. Manual adjustments stay.</summary>
    [RelayCommand]
    private Task RebuildPoints() => RunAsync(async () =>
    {
        if (_session.CurrentUser is not { } user) return "Sign in first.";
        var sync = await _points.RebuildAsync(user.Id, AppClock.Today);
        return $"Rebuilt the ledger (points {sync?.OldTotal} → {sync?.NewTotal}).";
    });

    // ===== Badges (Phase 25) =====
    public System.Collections.Generic.IReadOnlyList<string> BadgeIds { get; } =
        BadgeCatalog.All.Select(b => b.Id).Append(MonthlyChallenges.IdFor(AppClock.Today)).ToList();

    [ObservableProperty] public partial string? SelectedBadgeId { get; set; } = "streak";
    [ObservableProperty] public partial decimal? BadgeTier { get; set; } = 1;

    /// <summary>Stores the badge as just earned (unseen), so the unlock sheet shows on the next screen.</summary>
    [RelayCommand]
    private Task AwardBadge() => RunAsync(async () =>
    {
        if (_session.CurrentUser is not { } user) return "Sign in first.";
        if (SelectedBadgeId is not { } id || BadgeCatalog.Find(id) is not { } badge) return "Pick a badge.";
        var tier = badge.IsTiered ? Math.Clamp((int)(BadgeTier ?? 1), 1, BadgeCatalog.MaxTier) : 0;
        await GymApplication.Services.GetRequiredService<BadgeService>().AwardAsync(user.Id, id, tier);
        await _main.PresentBadgeNewsAsync();
        return badge.IsTiered ? $"Awarded {badge.Name} at {BadgeCatalog.TierName(badge, tier)}." : $"Awarded {badge.Name}.";
    });

    /// <summary>Clears every badge and the backfill flag: the next check stores the history again and shows its sheet.</summary>
    [RelayCommand]
    private Task ResetBadges() => RunAsync(async () =>
    {
        if (_session.CurrentUser is not { } user) return "Sign in first.";
        await GymApplication.Services.GetRequiredService<BadgeService>().ResetAsync(user.Id);
        return "Badges cleared. The next check stores your history again.";
    });

    [RelayCommand]
    private Task CheckBadges() => RunAsync(async () =>
    {
        if (_session.CurrentUser is not { } user) return "Sign in first.";
        var check = await GymApplication.Services.GetRequiredService<BadgeService>().CheckAsync(user.Id, AppClock.Today);
        await _main.PresentBadgeNewsAsync();
        return check.Backfilled ? "Stored the history." : $"{check.Unlocks.Count} new unlock(s).";
    });

    // ===== Data =====
    [RelayCommand]
    private void OpenDataFolder() => Process.Start(new ProcessStartInfo(BenchSettings.DataDir) { UseShellExecute = true });

    [RelayCommand] private void ResetEmpty() => Restart(() => _settings.PendingReset = DataReset.Empty);
    [RelayCommand] private void CopyRealData() => Restart(() => _settings.PendingReset = DataReset.CopyReal);

    /// <summary>Resets and the social mode take effect at the next start, when nothing has the database open and the
    /// services are wired again; so save the change, start a new copy and quit.</summary>
    private void Restart(Action change)
    {
        change();
        _settings.Save();

        var self = Environment.ProcessPath!;
        var args = new System.Collections.Generic.List<string>();
        if (System.IO.Path.GetFileNameWithoutExtension(self) == "dotnet")
            args.Add(typeof(Program).Assembly.Location);   // started as "dotnet GainStreak.TestBench.dll"
        args.AddRange(["--after", Environment.ProcessId.ToString(CultureInfo.InvariantCulture)]);
        Process.Start(self, args);

        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    // ===== Plumbing =====

    /// <summary>Runs an action, shows its message (or the error), then refreshes the status and reloads Home.</summary>
    private async Task RunAsync(Func<Task<string>> action, bool reloadHome = true)
    {
        try
        {
            Message = await action();
        }
        catch (Exception e)
        {
            Message = $"Failed: {e.Message}";
        }
        if (reloadHome && _session.IsLoggedIn) _main.ShowHome();
        await RefreshStatusAsync();
    }

    private async Task RefreshStatusAsync()
    {
        var today = AppClock.Today;
        ClockText = today.ToString("dddd, d MMMM yyyy", CultureInfo.InvariantCulture);
        OffsetText = _settings.DayOffset switch
        {
            0 => "real date",
            1 => "1 day ahead",
            -1 => "1 day behind",
            > 0 => $"{_settings.DayOffset} days ahead",
            _ => $"{-_settings.DayOffset} days behind"
        };
        PickedDate = today.ToDateTime(TimeOnly.MinValue);

        IsSignedIn = _session.CurrentUser is not null;
        if (_session.CurrentUser is not { } current)
        {
            UserText = "Not signed in";
            PointsText = StreakText = "";
            return;
        }

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == current.Id);
            if (user is null) { UserText = "Signed-in user no longer exists"; return; }

            var level = LevelCalculator.For(user.Points);
            var streak = await _streaks.GetAsync(user.Id, today);
            UserText = user.Username;
            PointsText = $"{user.Points} points · level {level.Number} ({level.Name})";
            StreakText = $"Streak {streak.Current} (best {user.LongestStreak}) · {streak.Status}" +
                         (streak.OwedDay is { } owed ? $" · owes {owed:ddd d MMM}" : "");
        }
        catch (Exception e)
        {
            UserText = $"Couldn't read the user: {e.Message}";
        }
    }
}
