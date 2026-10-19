using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;

namespace GymApp.Desktop.ViewModels;

/// <summary>A gym dot on the map: neutral, your gym (white) or today's gym (accent).</summary>
public record MapGymDot(int Id, string Name, double Latitude, double Longitude, bool IsHome, bool IsToday);

/// <summary>Your own pin: your initials at today's gym, in the accent colour.</summary>
public record MapMyPin(int GymId, double Latitude, double Longitude, string Initials);

/// <summary>The home city's box, so the map opens fitted to it.</summary>
public record MapBounds(double South, double West, double North, double East);

/// <summary>One row of the Friends card, or one visitor on the map's gym card.</summary>
public partial class MapFriendRowViewModel(FriendCheckIn c, DateOnly today, bool onMap, bool byGym) : ObservableObject
{
    public int FriendUserId => c.FriendUserId;
    public int GymId => c.GymId;
    public string Username => c.Username;
    public string Initials => FriendMapRules.Initials(c.Username);
    public string When => FriendMapRules.DescribeDay(c.Date, today);
    public bool IsToday => FriendMapRules.DaysAgo(c.Date, today) == 0;     // accent day badge
    public double Opacity => FriendMapRules.Opacity(FriendMapRules.DaysAgo(c.Date, today));
    public bool CanShow => onMap;                                         // other cities can't be clicked

    /// <summary>Second line: the gym in Recent, a hint in By gym (the gym is the group's header).</summary>
    public string Subtitle => byGym
        ? (onMap ? "Show on the map" : c.GymName)
        : c.IsCustomGym ? $"{c.GymName} (custom gym)"
        : onMap ? c.GymName
        : c.CityName is { } city ? $"{c.GymName} · {city}" : c.GymName;

    /// <summary>The row's gym is the one selected on the map: outlined.</summary>
    [ObservableProperty] public partial bool IsSelected { get; set; }
}

/// <summary>A section of the Friends card: "Today 2", or "World Class · your gym 350 m".</summary>
public class MapFriendGroupViewModel(FriendGroup g, DateOnly today, bool byGym)
{
    public string Title => g.Title;
    public FriendGroupKind Kind => g.Kind;
    public bool IsToday => g.Kind == FriendGroupKind.Today;
    public bool IsAway => !g.OnMap;

    /// <summary>Right side of the header: the count (Recent) or the distance (By gym).</summary>
    public string Meta => g.Kind switch
    {
        FriendGroupKind.Gym when g.DistanceKm is { } km => Geo.FormatDistance(km),
        FriendGroupKind.Gym => "",
        FriendGroupKind.City => "other city",
        _ => g.Rows.Count.ToString(CultureInfo.InvariantCulture)
    };

    public IReadOnlyList<MapFriendRowViewModel> Rows { get; } =
        g.Rows.Select(r => new MapFriendRowViewModel(r, today, g.OnMap, byGym)).ToList();
}

/// <summary>One gym in "Your check-ins", with a bar relative to the most visited one.</summary>
public record MapMonthRow(string Name, int Visits, double Share)
{
    public string VisitsText => Visits.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// The Map screen (FRIENDS_GYM_MAP.md): the friends map with your gyms, your gym and today's, friends' last check-ins,
/// your monthly check-ins, the "Visible to friends" and "Ask for gym" settings, and the gym picker dialog.
/// Friends only ever see the gym and the day, never a time; custom gyms are never pins.
/// </summary>
public partial class GymsViewModel : ViewModelBase
{
    private readonly GymService _gyms;
    private readonly ProfileService _profiles;
    private readonly FriendMapService _friendMap;
    private readonly GymSummaryService _summaries;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;

    private City? _homeCity;
    private GymListItem? _homeGym;
    private TodayGymInfo? _today;
    private IReadOnlyList<GymRow> _cityGyms = [];
    private IReadOnlyList<FriendCheckIn> _latestPerFriend = [];
    private IReadOnlyDictionary<int, int> _myVisits = new Dictionary<int, int>();
    private int? _undoGymId;     // today's gym before the last change, for the toast's Undo
    private bool _loading;
    private bool _pickTodayWhenLoaded;

    /// <summary>Opens the picker for today's gym as soon as the screen has loaded.</summary>
    public void PickTodayWhenLoaded() => _pickTodayWhenLoaded = true;

    public GymsViewModel(GymService gyms, ProfileService profiles, FriendMapService friendMap, GymSummaryService summaries,
        GymPickerViewModel picker, SessionService session, Func<MainViewModel> shell)
    {
        _gyms = gyms; _profiles = profiles; _friendMap = friendMap; _summaries = summaries;
        Picker = picker; _session = session; _shell = shell;
        (Year, Month) = (Today.Year, Today.Month);
        _ = LoadAsync();
    }

    private static DateOnly Today => AppClock.Today;
    private int UserId => _session.CurrentUser!.Id;

    /// <summary>Raised after anything drawn on the map changed (dots, pins, your pin, selection, bounds).</summary>
    public event EventHandler? MapChanged;

    // ===== Header =====
    [ObservableProperty] public partial string CityName { get; set; } = "";
    [ObservableProperty] public partial bool HasCity { get; set; }
    [ObservableProperty] public partial MapBounds? Bounds { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }

    // Settings chips: the whole chip is the switch
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShareStateText))]
    public partial bool ShareCheckIns { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AskStateText))]
    public partial bool AskForGym { get; set; } = true;

    public string ShareStateText => ShareCheckIns ? "On · gym and day only" : "Off";
    public string AskStateText => AskForGym ? "On · your gym preselected" : "Off · your gym is used";

    // ===== Friends map =====
    [ObservableProperty] public partial int RangeIndex { get; set; } = 2;   // Today, 7 days, 30 days
    [ObservableProperty] public partial int ModeIndex { get; set; }        // Latest, All visits
    [ObservableProperty] public partial int SortIndex { get; set; }        // Recent, By gym

    private MapRange Range => (MapRange)RangeIndex;
    private MapMode Mode => (MapMode)ModeIndex;
    private FriendSort Sort => (FriendSort)SortIndex;

    public string RangeWords => Range switch { MapRange.Today => "today", MapRange.Week => "in the last 7 days", _ => "in the last 30 days" };
    public string RangeTitle => Range switch { MapRange.Today => "Today", MapRange.Week => "Last 7 days", _ => "Last 30 days" };

    public IReadOnlyList<GymPin> Pins { get; private set; } = [];
    public IReadOnlyList<MapGymDot> GymDots { get; private set; } = [];
    public MapMyPin? MyPin { get; private set; }

    [ObservableProperty] public partial bool ShowMapEmpty { get; set; }
    [ObservableProperty] public partial string MapEmptyText { get; set; } = "";

    // Gym card (a pin or gym dot clicked)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial int? SelectedGymId { get; set; }

    public bool HasSelection => SelectedGymId is not null;
    public ObservableCollection<MapFriendRowViewModel> SelectedVisitors { get; } = new();
    [ObservableProperty] public partial string SelectedName { get; set; } = "";
    [ObservableProperty] public partial string SelectedDetail { get; set; } = "";
    [ObservableProperty] public partial bool HasSelectedVisitors { get; set; }
    [ObservableProperty] public partial string SelectedMine { get; set; } = "";
    [ObservableProperty] public partial string SelectedPickLabel { get; set; } = "";
    [ObservableProperty] public partial bool CanPickSelected { get; set; }
    [ObservableProperty] public partial bool IsSelectedToday { get; set; }

    // Toast after today's gym changed
    [ObservableProperty] public partial string? Toast { get; set; }
    [ObservableProperty] public partial bool CanUndoToast { get; set; }

    // ===== Your gym =====
    [ObservableProperty] public partial bool HasHomeGym { get; set; }
    [ObservableProperty] public partial string HomeGymName { get; set; } = "No gym chosen yet";
    [ObservableProperty] public partial string HomeGymDetail { get; set; } = "Choose it once; it's preselected at every check-in.";
    [ObservableProperty] public partial string HomeGymButton { get; set; } = "Choose";
    [ObservableProperty] public partial string TodayLabel { get; set; } = "";
    [ObservableProperty] public partial string TodayButton { get; set; } = "";

    // ===== Friends card =====
    public ObservableCollection<MapFriendGroupViewModel> FriendGroups { get; } = new();
    [ObservableProperty] public partial string FriendCountText { get; set; } = "";
    [ObservableProperty] public partial bool HasFriendRows { get; set; }

    public string FriendsEmptyText => Range == MapRange.Month
        ? "No friends' check-ins in the last 30 days."
        : $"No friends' check-ins {RangeWords}. Try 30 days.";

    // ===== Your check-ins =====
    public int Year { get; private set; }
    public int Month { get; private set; }
    public ObservableCollection<MapMonthRow> MonthRows { get; } = new();
    [ObservableProperty] public partial string MonthTitle { get; set; } = "";
    [ObservableProperty] public partial string MonthWorkouts { get; set; } = "0";
    [ObservableProperty] public partial string MonthAtGym { get; set; } = "0";
    [ObservableProperty] public partial string MonthNoGym { get; set; } = "0";
    [ObservableProperty] public partial string MonthGyms { get; set; } = "0";
    [ObservableProperty] public partial bool HasMonthRows { get; set; }
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(NextMonthCommand))] public partial bool CanGoNext { get; set; }

    // ===== Picker dialog =====
    public GymPickerViewModel Picker { get; }
    [ObservableProperty] public partial bool IsPickerOpen { get; set; }

    // ===== Loading =====

    private async Task LoadAsync()
    {
        try
        {
            _loading = true;
            var s = await _profiles.GetAsync(UserId);
            if (s is null) { Error = "Could not load the map."; return; }
            _homeCity = s.Profile.HomeCity;
            ShareCheckIns = s.Profile.ShareGymCheckIns == true;
            AskForGym = s.Profile.AskForGymAfterCheckIn;
            _loading = false;

            HasCity = _homeCity is not null;
            CityName = _homeCity?.Name ?? "No home city";
            Bounds = _homeCity is { } c ? new MapBounds(c.South, c.West, c.North, c.East) : null;
            _homeGym = s.Profile.HomeGymId is { } id ? await _gyms.GetPickableGymAsync(UserId, id) : null;
            _today = await _gyms.GetTodayAsync(UserId);
            _cityGyms = _homeCity is { } city ? (await _gyms.GetGymsForCityAsync(UserId, city.Id))?.Rows ?? [] : [];
            _myVisits = await _gyms.GetMonthVisitsAsync(UserId, Today.Year, Today.Month);

            ApplyHomeGym();
            await LoadFriendMapAsync();
            await LoadMonthAsync();
            if (_pickTodayWhenLoaded && _today?.LoggedToday == true) OpenTodayPickerNow();
            _pickTodayWhenLoaded = false;
        }
        catch (Exception)
        {
            Error = "Could not load the map.";
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Where distances are measured from: your gym if it has a position, else the city's centre.</summary>
    private (double, double)? Origin =>
        _homeGym is { Latitude: { } lat, Longitude: { } lon } ? (lat, lon)
        : _homeCity is { } c ? (c.CenterLatitude, c.CenterLongitude) : null;

    private async Task LoadFriendMapAsync()
    {
        try
        {
            var r = await _friendMap.GetAsync(UserId, _homeCity?.Id, _homeGym?.Id, Origin, Range, Mode, Sort);
            Pins = r.Layout.Pins;
            _latestPerFriend = r.Layout.LatestPerFriend;
            SetGroups(r.Groups);
            RebuildMap();
        }
        catch (Exception)
        {
            Error = "Could not load your friends' check-ins.";
        }
    }

    private void RebuildFriendGroups() =>
        SetGroups(FriendMapRules.GroupFriends(_latestPerFriend, Sort, _homeCity?.Id, _homeGym?.Id, Today));

    private void SetGroups(IReadOnlyList<FriendGroup> groups)
    {
        FriendGroups.Clear();
        foreach (var g in groups) FriendGroups.Add(new MapFriendGroupViewModel(g, Today, Sort == FriendSort.ByGym));
        HasFriendRows = FriendGroups.Count > 0;
        var n = _latestPerFriend.Count;
        FriendCountText = $"{n} {(n == 1 ? "friend" : "friends")} · {RangeTitle.ToLowerInvariant()}";
        MarkSelectedRows();
    }

    /// <summary>The gym dots, your pin, the empty-state text, the gym card; then tells the view to redraw.</summary>
    private void RebuildMap()
    {
        var todayGymId = _today?.Gym?.Id;
        GymDots = _cityGyms
            .Where(r => r.Gym is { Latitude: not null, Longitude: not null })
            .Select(r => new MapGymDot(r.Gym.Id, r.Gym.Name, r.Gym.Latitude!.Value, r.Gym.Longitude!.Value,
                r.Gym.Id == _homeGym?.Id, r.Gym.Id == todayGymId))
            .ToList();
        var todayDot = GymDots.FirstOrDefault(d => d.IsToday);
        var me = _session.CurrentUser?.Username ?? "";
        MyPin = todayDot is null ? null : new MapMyPin(todayDot.Id, todayDot.Latitude, todayDot.Longitude, FriendMapRules.Initials(me));

        ShowMapEmpty = !HasCity || Pins.Count == 0;
        MapEmptyText = !HasCity ? "Set your home city to see gyms and friends on the map."
                     : _latestPerFriend.Count > 0 ? $"Your friends checked in {RangeWords}, but not at gyms on this map. They're in the Friends list."
                     : ShareCheckIns ? $"No friends' check-ins {RangeWords}. Friends show up here when they share theirs."
                     : $"No friends' check-ins {RangeWords}. Turn on Visible to friends to share yours too.";

        if (SelectedGymId is { } id) ShowGymCard(id);
        MapChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyHomeGym()
    {
        HasHomeGym = _homeGym is not null;
        HomeGymName = _homeGym?.Name ?? "No gym chosen yet";
        HomeGymDetail = _homeGym is { } g
            ? string.Join(" · ", new[] { g.IsCustom ? "Custom gym" : null, g.Address }.Where(x => !string.IsNullOrEmpty(x)))
            : "Choose it once; it's preselected at every check-in.";
        if (HomeGymDetail.Length == 0) HomeGymDetail = CityName;
        HomeGymButton = HasHomeGym ? "Change" : "Choose";

        var logged = _today?.LoggedToday == true;
        TodayLabel = !logged ? "Not checked in yet"
                   : _today!.Gym is { } t ? $"{t.Name} ✓" : "Checked in, no gym logged";
        TodayButton = !logged ? "Check in" : _today!.Gym is null ? "Add gym" : _today.Gym.Id == _homeGym?.Id ? "Trained elsewhere?" : "Change";
    }

    private async Task LoadMonthAsync()
    {
        MonthTitle = GymSummaries.MonthTitle(Year, Month);
        var (ny, nm) = GymSummaries.Shift(Year, Month, 1);
        CanGoNext = !GymSummaries.IsFuture(ny, nm, Today);
        try
        {
            var s = await _summaries.GetMonthAsync(UserId, Year, Month);
            MonthWorkouts = s.TotalWorkouts.ToString(CultureInfo.InvariantCulture);
            MonthAtGym = (s.TotalWorkouts - s.NoGymLogged).ToString(CultureInfo.InvariantCulture);
            MonthNoGym = s.NoGymLogged.ToString(CultureInfo.InvariantCulture);
            MonthGyms = s.DistinctGyms.ToString(CultureInfo.InvariantCulture);
            MonthRows.Clear();
            var max = s.Gyms.Count > 0 ? s.Gyms[0].Visits : 1;
            foreach (var g in s.Gyms.Take(4)) MonthRows.Add(new MapMonthRow(g.Name, g.Visits, (double)g.Visits / max));
            HasMonthRows = MonthRows.Count > 0;
        }
        catch (Exception)
        {
            Error = "Could not load your check-ins.";
        }
    }

    [RelayCommand]
    private Task PreviousMonthAsync()
    {
        (Year, Month) = GymSummaries.Shift(Year, Month, -1);
        return LoadMonthAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private Task NextMonthAsync()
    {
        var (y, m) = GymSummaries.Shift(Year, Month, 1);
        if (GymSummaries.IsFuture(y, m, Today)) return Task.CompletedTask;
        (Year, Month) = (y, m);
        return LoadMonthAsync();
    }

    // ===== Toggles =====

    partial void OnRangeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(RangeWords));
        OnPropertyChanged(nameof(RangeTitle));
        OnPropertyChanged(nameof(FriendsEmptyText));
        _ = LoadFriendMapAsync();
    }

    partial void OnModeIndexChanged(int value) => _ = LoadFriendMapAsync();
    partial void OnSortIndexChanged(int value) => RebuildFriendGroups();   // no database call

    [RelayCommand] private void SetRange(string index) => RangeIndex = int.Parse(index, CultureInfo.InvariantCulture);
    [RelayCommand] private void SetMode(string index) => ModeIndex = int.Parse(index, CultureInfo.InvariantCulture);
    [RelayCommand] private void SetSort(string index) => SortIndex = int.Parse(index, CultureInfo.InvariantCulture);

    [RelayCommand] private void ToggleShare() => ShareCheckIns = !ShareCheckIns;
    [RelayCommand] private void ToggleAsk() => AskForGym = !AskForGym;

    partial void OnShareCheckInsChanged(bool value)
    {
        if (_loading) return;
        _ = SaveAsync(() => _profiles.SetShareGymCheckInsAsync(UserId, value), "Could not save that setting.");
        RebuildMap();
    }

    partial void OnAskForGymChanged(bool value)
    {
        if (_loading) return;
        _ = SaveAsync(() => _profiles.SetAskForGymAfterCheckInAsync(UserId, value), "Could not save that setting.");
    }

    private async Task SaveAsync(Func<Task<ProfileResult>> save, string failure)
    {
        try
        {
            var r = await save();
            Error = r.Success ? null : r.Error;
        }
        catch (Exception)
        {
            Error = failure;
        }
    }

    // ===== Selection and the gym card =====

    /// <summary>A pin or gym dot was clicked (or a Friends row): select it, or deselect when it's already selected.</summary>
    public void SelectGym(int? gymId)
    {
        SelectedGymId = SelectedGymId == gymId ? null : gymId;
        if (SelectedGymId is { } id) ShowGymCard(id);
        MarkSelectedRows();
        MapChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand] private void CloseGymCard() => SelectGym(null);

    [RelayCommand]
    private void ShowFriend(MapFriendRowViewModel row)
    {
        if (row.CanShow) SelectGym(row.GymId);
    }

    private void MarkSelectedRows()
    {
        foreach (var r in FriendGroups.SelectMany(g => g.Rows)) r.IsSelected = r.GymId == SelectedGymId && r.CanShow;
    }

    private void ShowGymCard(int gymId)
    {
        var row = _cityGyms.FirstOrDefault(r => r.Gym.Id == gymId);
        var pin = Pins.FirstOrDefault(p => p.GymId == gymId);
        SelectedName = row?.Gym.Name ?? pin?.GymName ?? "Gym";
        var distance = gymId == _homeGym?.Id ? "your gym"
            : row?.Gym is { Latitude: { } la, Longitude: { } lo } && Origin is { } o
            ? Geo.FormatDistance(Geo.HaversineKm(o.Item1, o.Item2, la, lo)) + (HasHomeGym ? " from your gym" : " from the centre")
            : null;
        SelectedDetail = string.Join(" · ", new[] { row?.Gym.Address, distance }.Where(x => !string.IsNullOrEmpty(x)));

        SelectedVisitors.Clear();
        foreach (var v in pin?.Visitors ?? []) SelectedVisitors.Add(new MapFriendRowViewModel(v, Today, true, false));
        HasSelectedVisitors = SelectedVisitors.Count > 0;

        var mine = _myVisits.GetValueOrDefault(gymId);
        SelectedMine = mine == 1 ? "1 visit" : $"{mine} visits";
        var logged = _today?.LoggedToday == true;
        IsSelectedToday = logged && _today!.Gym?.Id == gymId;
        CanPickSelected = !IsSelectedToday && row is not null;
        SelectedPickLabel = !logged ? "Check in first" : IsSelectedToday ? "Logged here today" : "I trained here today";
    }

    /// <summary>The gym card's button: today's gym becomes this one. Without a check-in today it goes to the
    /// Dashboard instead, since a check-in needs the 50% rule (the gym is asked right after).</summary>
    [RelayCommand]
    private async Task TrainedHereTodayAsync()
    {
        if (SelectedGymId is not { } id) return;
        if (_today?.LoggedToday != true) { _shell().ShowHome(); return; }
        await SetTodaysGymAsync(id, makeHome: false);
    }

    private async Task<bool> SetTodaysGymAsync(int? gymId, bool makeHome)
    {
        var before = _today?.Gym?.Id;
        var r = await _gyms.SetGymForTodayAsync(UserId, gymId);
        if (!r.Success) { Error = r.Error; return false; }
        if (makeHome && gymId is { } home) await SetHomeGymAsync(home);

        _undoGymId = before;
        CanUndoToast = true;
        var name = gymId is { } g ? (_cityGyms.FirstOrDefault(x => x.Gym.Id == g)?.Gym.Name
                                     ?? (await _gyms.GetPickableGymAsync(UserId, g))?.Name ?? "that gym") : null;
        Toast = name is null ? "Today's gym removed."
              : $"Today logged at {name}. " + (ShareCheckIns ? "Friends can see it." : "Only you can see it.");
        await ReloadTodayAsync();
        return true;
    }

    [RelayCommand]
    private async Task UndoToastAsync()
    {
        var r = await _gyms.SetGymForTodayAsync(UserId, _undoGymId);
        if (!r.Success) { Error = r.Error; return; }
        Toast = null;
        CanUndoToast = false;
        await ReloadTodayAsync();
    }

    [RelayCommand] private void DismissToast() => Toast = null;

    /// <summary>After today's gym changed: the Your gym card, your pin, your visits, the month and the gym card.</summary>
    private async Task ReloadTodayAsync()
    {
        _today = await _gyms.GetTodayAsync(UserId);
        _myVisits = await _gyms.GetMonthVisitsAsync(UserId, Today.Year, Today.Month);
        ApplyHomeGym();
        RebuildMap();
        await LoadMonthAsync();
    }

    private async Task SetHomeGymAsync(int gymId)
    {
        var r = await _profiles.SetHomeGymAsync(UserId, gymId);
        if (!r.Success) { Error = r.Error; return; }
        _homeGym = await _gyms.GetPickableGymAsync(UserId, gymId);
        ApplyHomeGym();
        RebuildFriendGroups();   // "· your gym" moves, and distances are measured from it
        await LoadFriendMapAsync();
    }

    // ===== The picker dialog =====

    private IReadOnlyDictionary<int, int> FriendsAtGym() =>
        _latestPerFriend.GroupBy(c => c.GymId).ToDictionary(g => g.Key, g => g.Count());

    [RelayCommand]
    private void OpenHomeGymPicker() =>
        OpenPicker(PickerMode.HomeGym, async (gym, _) =>
        {
            if (gym is null) return;
            await SetHomeGymAsync(gym.Id);
            if (Error is null) ClosePicker();
        });

    /// <summary>The "Today" line's button: pick today's gym, or check in first on the Dashboard.</summary>
    [RelayCommand]
    private void OpenTodayPicker()
    {
        if (_today?.LoggedToday != true) { _shell().ShowHome(); return; }
        OpenTodayPickerNow();
    }

    /// <summary>Also from Home's "Choose another…": the picker for today's gym, right away.</summary>
    public void OpenTodayPickerNow() =>
        OpenPicker(PickerMode.Today, async (gym, makeHome) =>
        {
            if (await SetTodaysGymAsync(gym?.Id, makeHome)) ClosePicker();
            else Picker.ShowError(Error ?? "Could not save the gym.");
        });

    [RelayCommand]
    private void ChangeCity() => OpenPicker(PickerMode.City, (_, _) => Task.CompletedTask);

    private void OpenPicker(PickerMode mode, Func<GymListItem?, bool, Task> chosen)
    {
        Error = null;
        Picker.Open(mode, _homeCity, _homeGym?.Id, _today?.Gym?.Id, _homeGym?.Name, FriendsAtGym(), chosen,
            ChooseHomeCityAsync, ClosePicker);
        IsPickerOpen = true;
    }

    private void ClosePicker() => IsPickerOpen = false;

    private async Task ChooseHomeCityAsync(City city)
    {
        var r = await _profiles.SetHomeCityAsync(UserId, city.Id);
        if (!r.Success) { Picker.ShowError(r.Error ?? "Could not save the city."); return; }
        ClosePicker();
        SelectedGymId = null;
        await LoadAsync();
    }
}
