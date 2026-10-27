using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;

namespace GymApp.App.ViewModels;

/// <summary>One gym in the picker list. Custom gyms (always the user's own) can be edited and deleted.
/// <paramref name="isCurrent"/>: it's the gym the dialog is choosing (your gym, or today's).</summary>
public partial class GymRowViewModel(GymRow row, bool isCurrent, bool isHome, int friendsHere, PickerMode mode,
    GymPickerViewModel owner) : ObservableObject
{
    public GymListItem Gym => row.Gym;
    public bool HasPosition => row.Gym is { Latitude: not null, Longitude: not null };
    public string Name => row.Gym.Name;
    public string? Address => row.Gym.Address;
    public bool HasAddress => !string.IsNullOrEmpty(row.Gym.Address);
    // A looked-up address is the nearest street to the gym's position, so it's only approximate
    public string AddressText => row.Gym.AddressIsApproximate ? $"Near {row.Gym.Address}" : row.Gym.Address ?? "";
    public string DistanceText => row.DistanceKm is { } km ? Geo.FormatDistance(km) : "";
    public bool IsCustom => row.Gym.IsCustom;
    public bool IsCurrent { get; } = isCurrent;
    public bool IsHome { get; } = isHome;
    public bool HasFriends => friendsHere > 0;
    public string FriendsLabel => friendsHere == 1 ? "1 friend" : $"{friendsHere} friends";
    public string PickLabel => mode == PickerMode.HomeGym ? (IsCurrent ? "Current" : "Set") : (IsCurrent ? "Picked" : "Pick");

    /// <summary>"1.2 km · Calea Aradului 1": the second line of the row.</summary>
    public string DetailText => string.Join(" · ", new[] { DistanceText, AddressText }.Where(x => x.Length > 0));

    [ObservableProperty] public partial bool IsConfirmingDelete { get; set; }

    [RelayCommand(CanExecute = nameof(CanPick))] private Task Pick() => owner.PickAsync(row);
    private bool CanPick() => !IsCurrent;

    [RelayCommand] private void Edit() => owner.StartEditGym(this);
    [RelayCommand] private void Delete() => IsConfirmingDelete = true;
    [RelayCommand] private void CancelDelete() => IsConfirmingDelete = false;
    [RelayCommand] private Task ConfirmDelete() => owner.DeleteGymAsync(this);
}

/// <summary>What the picker dialog is for: choosing your gym, today's gym, or (from the Map header) the home city.</summary>
public enum PickerMode { HomeGym, Today, City }

/// <summary>One result of the custom gym address search.</summary>
public partial class AddressCandidateViewModel(AddressCandidate candidate, Action<AddressCandidate> pick) : ObservableObject
{
    public string Text => candidate.Text;
    public string DisplayName => candidate.DisplayName;

    [RelayCommand] private void Pick() => pick(candidate);
}

/// <summary>
/// The gym picker dialog on the Map screen. It chooses your gym, today's gym, or a home city (<see cref="PickerMode"/>)
/// and hands the choice back to the screen, which saves it. The list always comes from the database, so it works
/// offline; gyms are fetched in the background on first use of a city, after 30 days, or on Refresh.
/// </summary>
public partial class GymPickerViewModel : ViewModelBase
{
    private readonly GymService _gyms;
    private readonly GymFetchService _fetch;
    private readonly GymAddressLookupService _lookup;
    private readonly GeocodingService _geocoding;
    private readonly CityService _cities;
    private readonly ProfileService _profiles;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;

    private int? _cityId;
    private City? _city;
    private int? _homeCityId;
    private int? _currentGymId;
    private int? _homeGymId;
    private IReadOnlyDictionary<int, int> _friendsAtGym = new Dictionary<int, int>();
    private Func<GymListItem?, bool, Task> _chosen = (_, _) => Task.CompletedTask;
    private Func<City, Task> _cityChosen = _ => Task.CompletedTask;
    private Action _closed = () => { };

    public GymPickerViewModel(GymService gyms, GymFetchService fetch, GymAddressLookupService lookup, GeocodingService geocoding,
        CityService cities, ProfileService profiles, SessionService session, Func<MainViewModel> shell)
    {
        _gyms = gyms; _fetch = fetch; _lookup = lookup; _geocoding = geocoding; _cities = cities; _profiles = profiles;
        _session = session; _shell = shell;
    }

    [ObservableProperty] public partial PickerMode Mode { get; set; }
    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string Subtitle { get; set; } = "";

    /// <summary>Today's pick, while you have no gym yet: also make it your gym (checked the first time).</summary>
    [ObservableProperty] public partial bool ShowMakeMyGym { get; set; }
    [ObservableProperty] public partial bool MakeMyGym { get; set; } = true;

    /// <summary>Opens the dialog. <paramref name="friendsAtGym"/>: friends whose latest check-in is at each gym, for
    /// the "N friends" badges. <paramref name="chosen"/> gets the gym (null = today without a gym) and whether to also
    /// make it your gym; <paramref name="cityChosen"/> a city in City mode.</summary>
    public void Open(PickerMode mode, City? city, int? homeGymId, int? todayGymId, string? homeGymName,
        IReadOnlyDictionary<int, int> friendsAtGym, Func<GymListItem?, bool, Task> chosen, Func<City, Task> cityChosen,
        Action closed)
    {
        Mode = mode;
        _homeGymId = homeGymId;
        _currentGymId = mode == PickerMode.HomeGym ? homeGymId : todayGymId;
        HasCurrentGym = mode == PickerMode.Today && todayGymId is not null;
        _friendsAtGym = friendsAtGym;
        _chosen = chosen;
        _cityChosen = cityChosen;
        _closed = closed;
        _homeCityId = city?.Id;
        Error = Info = null;
        IsGymFormOpen = false;
        SearchText = "";
        ShowMakeMyGym = mode == PickerMode.Today && homeGymId is null;
        MakeMyGym = true;

        (Title, Subtitle) = mode switch
        {
            PickerMode.HomeGym => ("Choose your gym", "Your gym is preselected after every check-in. You only set it once."),
            PickerMode.Today => ("Where did you train today?",
                homeGymName is null ? "Only today's log changes." : $"Only today's log changes. Your gym stays {homeGymName}."),
            _ => ("Change city", "The map and the gym list open on this city.")
        };

        if (mode == PickerMode.City || city is null)
        {
            HasCity = city is not null;
            StartChoosingCity(mode == PickerMode.City ? "Search for your city." : "Which city is your gym in?", saveAsHomeDefault: true);
            return;
        }
        _ = OpenCityAsync(city);
    }

    [RelayCommand] private void Close() => _closed();

    private int UserId => _session.CurrentUser!.Id;

    public ObservableCollection<GymRowViewModel> Gyms { get; } = new();
    public ObservableCollection<CityCandidateViewModel> CityCandidates { get; } = new();
    public ObservableCollection<AddressCandidateViewModel> AddressCandidates { get; } = new();

    // ===== City =====
    [ObservableProperty] public partial string CityName { get; set; } = "";
    [ObservableProperty] public partial bool HasCity { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowList))] public partial bool IsChoosingCity { get; set; }
    [ObservableProperty] public partial string CitySearchText { get; set; } = "";
    [ObservableProperty] public partial string? CitySearchMessage { get; set; }
    [ObservableProperty] public partial bool HasCityCandidates { get; set; }
    [ObservableProperty] public partial bool SaveAsHomeCity { get; set; }
    [ObservableProperty] public partial string CityPrompt { get; set; } = "";

    public bool ShowList => HasCity && !IsChoosingCity;

    // ===== List =====
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial bool SortByName { get; set; } = true;
    [ObservableProperty] public partial bool SortByDistance { get; set; }
    [ObservableProperty] public partial bool HasGyms { get; set; }
    [ObservableProperty] public partial string EmptyText { get; set; } = "";
    [ObservableProperty] public partial bool HasCurrentGym { get; set; }

    [ObservableProperty] public partial string CountText { get; set; } = "";

    /// <summary>For the A–Z / Nearest pill.</summary>
    public int SortIndex => SortByDistance ? 1 : 0;
    [RelayCommand] private void SortName() => SortByName = true;
    [RelayCommand] private void SortNearest() => SortByDistance = true;

    // ===== Fetch status =====
    [ObservableProperty] public partial string LastUpdatedText { get; set; } = "";
    [ObservableProperty] public partial bool IsFetching { get; set; }

    /// <summary>The fetch has run longer than <see cref="SlowFetchAfter"/>: the loading line says so and offers to
    /// start over (the public servers can take minutes when busy, and a fresh request often answers at once).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FetchingText))]
    public partial bool IsFetchSlow { get; set; }

    public TimeSpan SlowFetchAfter { get; set; } = TimeSpan.FromSeconds(15);   // shorter in the tests
    public string FetchingText => IsFetchSlow ? "Still loading. The map servers are busy right now." : "Loading gyms from OpenStreetMap…";
    private CancellationTokenSource? _fetchCts;   // the running fetch; a newer one cancels it
    [ObservableProperty] public partial string? RefreshNote { get; set; }
    [ObservableProperty] public partial string? FetchError { get; set; }
    [ObservableProperty] public partial string? AddressLookupText { get; set; }
    [ObservableProperty] public partial bool CanRetryAddressLookup { get; set; }
    [ObservableProperty] public partial bool CanRetryCitySearch { get; set; }
    [ObservableProperty] public partial bool CanRetryAddressSearch { get; set; }


    // ===== Custom gym form (add or edit) =====
    [ObservableProperty] public partial bool IsGymFormOpen { get; set; }
    [ObservableProperty] public partial string GymFormTitle { get; set; } = "";
    [ObservableProperty] public partial string GymFormName { get; set; } = "";
    [ObservableProperty] public partial string? GymFormError { get; set; }
    [ObservableProperty] public partial string? GymFormNameError { get; set; }
    [ObservableProperty] public partial string? GymFormAddressError { get; set; }
    private int? _editingGymId;   // null = adding a new one

    // The address can only come from a search result inside the city (plus an optional house number)
    [ObservableProperty] public partial string AddressQuery { get; set; } = "";
    [ObservableProperty] public partial string? AddressSearchMessage { get; set; }
    [ObservableProperty] public partial bool HasAddressCandidates { get; set; }
    [ObservableProperty] public partial string FormAddressText { get; set; } = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowAddressSearch))] public partial bool HasFormAddress { get; set; }
    [ObservableProperty] public partial bool ShowHouseNumber { get; set; }
    [ObservableProperty] public partial string FormHouseNumber { get; set; } = "";
    public bool ShowAddressSearch => !HasFormAddress;
    private AddressCandidate? _pickedCandidate;   // picked in this form
    private bool _addressChanged;                  // false = keep the gym's current address as it is

    public int MaxNameLength => CustomGyms.MaxNameLength;
    public int MaxHouseNumberLength => Addresses.MaxHouseNumberLength;

    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial string? Info { get; set; }

    private async Task OpenCityAsync(City city)
    {
        try
        {
            _cityId = city.Id;
            _city = city;
            CityName = city.Name;
            AddressLookupText = null;
            HasCity = true;
            IsChoosingCity = false;
            FetchError = null;
            RefreshNote = null;
            ApplyFetchedAt(city.GymsFetchedAt);

            await ReloadListAsync();
            await RunFetchAsync(city.Id, force: false);
            await RunAddressLookupAsync(city.Id);

        }
        catch (Exception)
        {
            Error = "Could not open that city.";
        }
    }

    /// <summary>Fills in approximate addresses for gyms OSM has none for, one per second, in the background.
    /// Only gyms never looked up are sent, so after the first time this is instant and offline.</summary>
    private async Task RunAddressLookupAsync(int cityId)
    {
        try
        {
            CanRetryAddressLookup = false;
            var missing = await _lookup.CountMissingAsync(cityId);
            if (missing == 0 || cityId != _cityId) return;

            AddressLookupText = $"Looking up addresses for {missing} gyms…";
            var progress = new Progress<AddressLookupProgress>(p =>
            {
                if (cityId != _cityId) return;
                AddressLookupText = $"Looking up addresses… {p.Done} of {p.Total}";
                if (p.Done % 5 == 0) _ = ReloadListAsync();   // show them as they arrive, without redrawing every second
            });

            var r = await _lookup.FillMissingAsync(cityId, progress);
            if (cityId != _cityId) return;
            AddressLookupText = r.Error is null ? null : "Couldn't look up every address. The rest will be tried next time.";
            CanRetryAddressLookup = r.Error is not null;
            await ReloadListAsync();
        }
        catch (Exception)
        {
            if (cityId == _cityId) AddressLookupText = null;   // addresses are a nice-to-have; the list still works
        }
    }

    private async Task ReloadListAsync()
    {
        try
        {
            if (_cityId is not { } cityId) return;
            var sort = SortByDistance ? GymSort.Distance : GymSort.Name;
            var r = await _gyms.GetGymsForCityAsync(UserId, cityId, SearchText, sort);
            if (r is null || cityId != _cityId) return;   // the city changed while loading

            Gyms.Clear();
            foreach (var row in r.Rows)
                Gyms.Add(new GymRowViewModel(row, row.Gym.Id == _currentGymId, row.Gym.Id == _homeGymId,
                    _friendsAtGym.GetValueOrDefault(row.Gym.Id), Mode, this));
            HasGyms = Gyms.Count > 0;
            CountText = Gyms.Count == 1 ? "1 gym" : $"{Gyms.Count} gyms";
            EmptyText = !string.IsNullOrWhiteSpace(SearchText) ? $"No gyms match \"{SearchText.Trim()}\"."
                      : r.City.GymsFetchedAt is null ? ""
                      : "No gyms found in this city on OpenStreetMap.";

        }
        catch (Exception)
        {
            Error = "Could not load the gym list.";
        }
    }

    /// <summary>Fetches in the background and reloads the list from the database when something changed.
    /// Opening the picker never waits on this: the cached list is already shown.</summary>
    private async Task RunFetchAsync(int cityId, bool force)
    {
        _fetchCts?.Cancel();
        var cts = _fetchCts = new CancellationTokenSource();
        IsFetching = true;
        IsFetchSlow = false;
        FetchError = null;
        RefreshNote = null;
        _ = MarkSlowAsync(cts.Token);
        try
        {
            var r = force ? await _fetch.RefreshAsync(cityId, cts.Token) : await _fetch.RefreshIfNeededAsync(cityId, cts.Token);
            if (cityId != _cityId) return;

            ApplyFetchedAt(r.FetchedAt);
            switch (r.Outcome)
            {
                case GymRefreshOutcome.Fetched:
                    await ReloadListAsync();
                    break;
                case GymRefreshOutcome.FailedWithCache:
                    RefreshNote = $"Couldn't refresh. {r.Error}";
                    break;
                case GymRefreshOutcome.Failed:
                    FetchError = r.Error;
                    break;
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // "Try again" or another city started a newer fetch, which owns the status now
        }
        catch (Exception)
        {
            if (cityId == _cityId) FetchError = "Couldn't load gyms.";
        }
        finally
        {
            if (_fetchCts == cts)
            {
                IsFetching = false;
                IsFetchSlow = false;
                _fetchCts = null;
            }
            cts.Cancel();   // stops the slow timer
        }
    }

    private async Task MarkSlowAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(SlowFetchAfter, ct);
            IsFetchSlow = true;
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>"Try again" on a slow fetch: drops the waiting request and sends a new one.</summary>
    [RelayCommand]
    private async Task RestartFetchAsync()
    {
        if (_cityId is not { } id) return;
        await RunFetchAsync(id, force: true);
        await RunAddressLookupAsync(id);
    }

    private void ApplyFetchedAt(DateTime? utc) =>
        LastUpdatedText = utc is { } at
            ? $"Last updated {DateTime.SpecifyKind(at, DateTimeKind.Utc).ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture)}"
            : "Not loaded yet";

    partial void OnSearchTextChanged(string value) => _ = ReloadListAsync();   // database only, so per keystroke is fine

    partial void OnSortByDistanceChanged(bool value)
    {
        SortByName = !value;
        OnPropertyChanged(nameof(SortIndex));
        _ = ReloadListAsync();
    }

    partial void OnSortByNameChanged(bool value) => SortByDistance = !value;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            if (_cityId is not { } id) return;
            await RunFetchAsync(id, force: true);
            await RunAddressLookupAsync(id);   // new or moved gyms

        }
        catch (Exception)
        {
            FetchError = "Couldn't load gyms.";
        }
    }

    [RelayCommand]
    private Task RetryAsync() => RefreshAsync();

    [RelayCommand]
    private Task RetryAddressLookupAsync() => _cityId is { } id ? RunAddressLookupAsync(id) : Task.CompletedTask;

    /// <summary>The screen saves the choice; the dialog closes when it worked.</summary>
    internal async Task PickAsync(GymRow row)
    {
        try
        {
            Error = null;
            await _chosen(row.Gym, ShowMakeMyGym && MakeMyGym);
        }
        catch (Exception)
        {
            Error = "Could not save the gym.";
        }
    }

    /// <summary>Today only: log today's workout without a gym.</summary>
    [RelayCommand]
    private async Task ClearGymAsync()
    {
        try
        {
            Error = null;
            await _chosen(null, false);
        }
        catch (Exception)
        {
            Error = "Could not clear the gym.";
        }
    }

    /// <summary>The screen shows an error the dialog can't (it already closed, or saving failed).</summary>
    internal void ShowError(string message) => Error = message;

    // ===== Custom gyms =====

    [RelayCommand]
    private void StartAddGym()
    {
        _editingGymId = null;
        GymFormTitle = $"Add a gym in {CityName}";
        // Start from what was searched for, since that's usually the gym that wasn't found
        GymFormName = SearchText.Trim();
        ResetFormAddress(null);
        GymFormError = GymFormNameError = GymFormAddressError = null;
        Info = null;
        IsGymFormOpen = true;
    }

    internal void StartEditGym(GymRowViewModel row)
    {
        _editingGymId = row.Gym.Id;
        GymFormTitle = $"Edit {row.Name}";
        GymFormName = row.Name;
        ResetFormAddress(row.Address);
        GymFormError = GymFormNameError = GymFormAddressError = null;
        Info = null;
        IsGymFormOpen = true;
    }

    [RelayCommand]
    private void CancelGymForm() => IsGymFormOpen = false;

    private void ResetFormAddress(string? current)
    {
        _addressChanged = false;
        _pickedCandidate = null;
        FormAddressText = current ?? "";
        HasFormAddress = current is not null;
        ShowHouseNumber = false;
        FormHouseNumber = "";
        AddressQuery = "";
        AddressSearchMessage = null;
        AddressCandidates.Clear();
        HasAddressCandidates = false;
    }

    // Button or Enter only: Nominatim doesn't allow search-as-you-type
    [RelayCommand]
    private async Task SearchAddressAsync()
    {
        try
        {
            if (_city is not { } city) return;
            AddressSearchMessage = null;
            CanRetryAddressSearch = false;
            AddressCandidates.Clear();
            HasAddressCandidates = false;

            var query = AddressQuery.Trim();
            var r = await _geocoding.SearchAddressAsync(query, city);
            if (!r.Success) { AddressSearchMessage = r.Error; CanRetryAddressSearch = r.CanRetry; return; }
            if (r.Candidates.Count == 0) { AddressSearchMessage = $"No addresses found for \"{query}\" in {city.Name}."; return; }

            foreach (var c in r.Candidates) AddressCandidates.Add(new AddressCandidateViewModel(c, PickAddress));
            HasAddressCandidates = true;

        }
        catch (Exception)
        {
            AddressSearchMessage = "Could not search for addresses.";
        }
    }

    private void PickAddress(AddressCandidate c)
    {
        _pickedCandidate = c;
        _addressChanged = true;
        FormAddressText = c.Text;
        HasFormAddress = true;
        ShowHouseNumber = !c.HasHouseNumber;   // OSM often knows the street but not the number
        FormHouseNumber = "";
        AddressCandidates.Clear();
        HasAddressCandidates = false;
        AddressSearchMessage = null;
    }

    // Shows the search again; the current address stays (and is saved) until another one is picked
    [RelayCommand]
    private void ChangeFormAddress() => HasFormAddress = false;

    [RelayCommand]
    private void ClearFormAddress()
    {
        ResetFormAddress(null);
        _addressChanged = true;
    }

    /// <summary>The address to save: a newly picked one (with the typed house number), or none. Only used when
    /// the address changed; otherwise the gym keeps its current one.</summary>
    private PickedAddress? FormAddress(out string? error)
    {
        error = null;
        if (_pickedCandidate is not { } c) return null;
        var number = c.HouseNumber ?? (string.IsNullOrWhiteSpace(FormHouseNumber) ? null : FormHouseNumber.Trim());
        if (Addresses.CheckHouseNumber(number) is { } e) { error = e; return null; }
        return new PickedAddress(Addresses.Format(c.Road, number, c.Area), c.Latitude, c.Longitude);
    }

    [RelayCommand]
    private async Task SaveGymFormAsync()
    {
        try
        {
            if (_cityId is not { } cityId) return;
            GymFormError = GymFormNameError = GymFormAddressError = null;
            var address = FormAddress(out var addressError);
            if (addressError is not null) { GymFormAddressError = addressError; return; }

            var r = _editingGymId is { } id
                ? await _gyms.UpdateCustomGymAsync(UserId, id, GymFormName, address, keepAddress: !_addressChanged)
                : await _gyms.AddCustomGymAsync(UserId, cityId, GymFormName, address);
            if (!r.Success)
            {
                switch (r.Field)
                {
                    case FormField.Name: GymFormNameError = r.Error; break;
                    case FormField.Address: GymFormAddressError = r.Error; break;
                    default: GymFormError = r.Error; break;
                }
                return;
            }

            IsGymFormOpen = false;
            Error = null;
            Info = _editingGymId is not null ? "Gym updated."
                 : r.Reactivated ? "Added back. Its earlier visits are still linked to it."
                 : Mode == PickerMode.HomeGym ? "Gym added. Set it as your gym." : "Gym added. Pick it to log it for today.";
            SearchText = "";   // also reloads, so the new gym shows up
            await ReloadListAsync();

        }
        catch (Exception)
        {
            GymFormError = "Could not save the gym.";
        }
    }

    internal async Task DeleteGymAsync(GymRowViewModel row)
    {
        try
        {
            var r = await _gyms.DeleteCustomGymAsync(UserId, row.Gym.Id);
            if (!r.Success) { row.IsConfirmingDelete = false; Error = r.Error; return; }

            Error = null;
            Info = r.Outcome == CustomGymDeleteOutcome.Deleted
                ? $"Deleted {row.Name}."
                : $"Removed {row.Name} from the list. It stays in your history because you logged workouts there.";
            if (row.Gym.Id == _currentGymId) { _currentGymId = null; HasCurrentGym = false; }
            await ReloadListAsync();

        }
        catch (Exception)
        {
            Error = "Could not delete the gym.";
        }
    }

    // ===== Choosing a city =====

    private void StartChoosingCity(string prompt, bool saveAsHomeDefault)
    {
        CityPrompt = prompt;
        SaveAsHomeCity = saveAsHomeDefault;
        CitySearchText = "";
        CitySearchMessage = null;
        CityCandidates.Clear();
        HasCityCandidates = false;
        IsChoosingCity = true;
    }

    [RelayCommand]
    private void ChangeCity() => StartChoosingCity("Search for another city.", saveAsHomeDefault: false);

    [RelayCommand]
    private void CancelChangeCity()
    {
        if (Mode == PickerMode.City || !HasCity) _closed();   // nothing else to show
        else IsChoosingCity = false;
    }

    [RelayCommand]
    private async Task SearchCityAsync()
    {
        try
        {
            CitySearchMessage = null;
            CanRetryCitySearch = false;
            CityCandidates.Clear();
            HasCityCandidates = false;

            var query = CitySearchText.Trim();
            var r = await _geocoding.SearchAsync(query);
            if (!r.Success) { CitySearchMessage = r.Error; CanRetryCitySearch = r.CanRetry; return; }
            if (r.Candidates.Count == 0) { CitySearchMessage = $"No cities found for \"{query}\"."; return; }

            foreach (var c in r.Candidates) CityCandidates.Add(new CityCandidateViewModel(c, ChooseCityAsync));
            HasCityCandidates = true;

        }
        catch (Exception)
        {
            CitySearchMessage = "Could not search for cities.";
        }
    }

    private async Task ChooseCityAsync(CityCandidate candidate)
    {
        try
        {
            var city = await _cities.UpsertAsync(candidate);
            if (Mode == PickerMode.City) { await _cityChosen(city); return; }   // the screen saves it as home
            if (SaveAsHomeCity && city.Id != _homeCityId)
            {
                var r = await _profiles.SetHomeCityAsync(UserId, city.Id);
                if (r.Success) _homeCityId = city.Id;
                else Error = r.Error;   // the picker still works with the city
            }
            SearchText = "";
            await OpenCityAsync(city);
        }
        catch (Exception)
        {
            Error = "Could not open that city.";
        }
    }

}
