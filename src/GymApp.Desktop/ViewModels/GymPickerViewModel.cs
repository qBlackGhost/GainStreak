using System;
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

/// <summary>One gym in the picker list. Custom gyms (always the user's own) can be edited and deleted.</summary>
public partial class GymRowViewModel(GymRow row, bool isCurrent, GymPickerViewModel owner) : ObservableObject
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
    public string PickLabel => IsCurrent ? "Current" : "Pick";

    [ObservableProperty] public partial bool IsConfirmingDelete { get; set; }

    [RelayCommand(CanExecute = nameof(CanPick))] private Task Pick() => owner.PickAsync(row);
    private bool CanPick() => !IsCurrent;

    [RelayCommand] private void Edit() => owner.StartEditGym(this);
    [RelayCommand] private void Delete() => IsConfirmingDelete = true;
    [RelayCommand] private void CancelDelete() => IsConfirmingDelete = false;
    [RelayCommand] private Task ConfirmDelete() => owner.DeleteGymAsync(this);
}

/// <summary>The city's box and centre, for the map view.</summary>
public record CityBox(double South, double West, double North, double East, double CenterLatitude, double CenterLongitude);

/// <summary>One result of the custom gym address search.</summary>
public partial class AddressCandidateViewModel(AddressCandidate candidate, Action<AddressCandidate> pick) : ObservableObject
{
    public string Text => candidate.Text;
    public string DisplayName => candidate.DisplayName;

    [RelayCommand] private void Pick() => pick(candidate);
}

/// <summary>Picks the gym for today's workout log. The list always comes from the database, so it works offline;
/// gyms are fetched in the background on first use of a city, after 30 days, or on Refresh.</summary>
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

    public GymPickerViewModel(GymService gyms, GymFetchService fetch, GymAddressLookupService lookup, GeocodingService geocoding,
        CityService cities, ProfileService profiles, SessionService session, Func<MainViewModel> shell)
    {
        _gyms = gyms; _fetch = fetch; _lookup = lookup; _geocoding = geocoding; _cities = cities; _profiles = profiles;
        _session = session; _shell = shell;
        _ = LoadAsync();
    }

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

    // ===== List or map =====
    [ObservableProperty] public partial bool ShowAsList { get; set; } = true;
    [ObservableProperty] public partial bool ShowAsMap { get; set; }
    [ObservableProperty] public partial CityBox? MapBox { get; set; }
    [ObservableProperty] public partial string? NoPositionText { get; set; }
    [ObservableProperty] public partial string CurrentGymText { get; set; } = "";

    // ===== Fetch status =====
    [ObservableProperty] public partial string LastUpdatedText { get; set; } = "";
    [ObservableProperty] public partial bool IsFetching { get; set; }
    [ObservableProperty] public partial string? RefreshNote { get; set; }
    [ObservableProperty] public partial string? FetchError { get; set; }
    [ObservableProperty] public partial string? AddressLookupText { get; set; }
    [ObservableProperty] public partial bool CanRetryAddressLookup { get; set; }
    [ObservableProperty] public partial bool CanRetryCitySearch { get; set; }
    [ObservableProperty] public partial bool CanRetryAddressSearch { get; set; }

    /// <summary>Shown over the map when its tiles can't be downloaded (offline). The list still works.</summary>
    [ObservableProperty] public partial string? MapTilesNote { get; set; }

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

    private async Task LoadAsync()
    {
        try
        {
            var today = await _gyms.GetTodayAsync(UserId);
            if (!today.LoggedToday)
            {
                Error = "Log today's workout on Home first, then pick the gym.";
                return;
            }
            _currentGymId = today.Gym?.Id;
            HasCurrentGym = today.Gym is not null;
            CurrentGymText = today.Gym is { } g ? $"Today: {g.Name}" : "";

            var home = await _profiles.GetHomeCityAsync(UserId);
            _homeCityId = home?.Id;
            if (home is null)
            {
                // No home city yet: ask for a city first and offer (not force) to save it as home
                StartChoosingCity("Which city is your gym in?", saveAsHomeDefault: true);
                return;
            }
            await OpenCityAsync(home);
        }
        catch (Exception)
        {
            Error = "Could not load the gym list.";
        }
    }

    private async Task OpenCityAsync(City city)
    {
        try
        {
            _cityId = city.Id;
            _city = city;
            MapBox = new CityBox(city.South, city.West, city.North, city.East, city.CenterLatitude, city.CenterLongitude);
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
            foreach (var row in r.Rows) Gyms.Add(new GymRowViewModel(row, row.Gym.Id == _currentGymId, this));
            HasGyms = Gyms.Count > 0;
            var unplaced = Gyms.Count(g => !g.HasPosition);
            NoPositionText = unplaced == 0 ? null
                : unplaced == 1 ? "1 gym has no map position, so it's only in the list."
                : $"{unplaced} gyms have no map position, so they're only in the list.";
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
        IsFetching = true;
        FetchError = null;
        RefreshNote = null;
        try
        {
            var r = force ? await _fetch.RefreshAsync(cityId) : await _fetch.RefreshIfNeededAsync(cityId);
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
        catch (Exception)
        {
            if (cityId == _cityId) FetchError = "Couldn't load gyms.";
        }
        finally
        {
            if (cityId == _cityId) IsFetching = false;
        }
    }

    private void ApplyFetchedAt(DateTime? utc) =>
        LastUpdatedText = utc is { } at
            ? $"Last updated {DateTime.SpecifyKind(at, DateTimeKind.Utc).ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture)}"
            : "Not loaded yet";

    partial void OnSearchTextChanged(string value) => _ = ReloadListAsync();   // database only, so per keystroke is fine

    partial void OnSortByDistanceChanged(bool value)
    {
        SortByName = !value;
        _ = ReloadListAsync();
    }

    partial void OnSortByNameChanged(bool value) => SortByDistance = !value;

    partial void OnShowAsListChanged(bool value) => ShowAsMap = !value;
    partial void OnShowAsMapChanged(bool value) => ShowAsList = !value;

    /// <summary>A marker on the map was clicked: same as Pick in the list.</summary>
    internal void PickFromMap(GymRowViewModel row)
    {
        if (row.PickCommand.CanExecute(null)) row.PickCommand.Execute(null);
    }

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

    /// <summary>From the map view: whether the last tile download worked.</summary>
    internal void MapTilesLoaded(bool ok) =>
        MapTilesNote = ok ? null : "Map tiles need an internet connection. The gym list still works.";

    internal async Task PickAsync(GymRow row)
    {
        try
        {
            var r = await _gyms.SetGymForTodayAsync(UserId, row.Gym.Id);
            if (!r.Success) { Error = r.Error; return; }
            _shell().ShowHome();

        }
        catch (Exception)
        {
            Error = "Could not save the gym.";
        }
    }

    [RelayCommand]
    private async Task ClearGymAsync()
    {
        try
        {
            var r = await _gyms.SetGymForTodayAsync(UserId, null);
            if (!r.Success) { Error = r.Error; return; }
            _shell().ShowHome();

        }
        catch (Exception)
        {
            Error = "Could not clear the gym.";
        }
    }

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
                 : "Gym added. Pick it to log it for today.";
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
    private void CancelChangeCity() => IsChoosingCity = false;

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

    [RelayCommand]
    private void Back() => _shell().ShowHome();
}
