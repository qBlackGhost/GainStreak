using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Core;
using GymApp.Services;

namespace GymApp.App.ViewModels;

public enum AddFoodPage { Search, Amount, Form, Quick, Barcode }

/// <summary>One of Breakfast / Lunch / Dinner / Snacks in the sheet's pill.</summary>
public partial class MealOptionViewModel(Meal meal, AddFoodViewModel owner) : ObservableObject
{
    public Meal Meal { get; } = meal;
    public string Name => MealDefaults.DisplayName(Meal);
    [ObservableProperty] public partial bool IsSelected { get; set; }
    [RelayCommand] private void Pick() => owner.PickMeal(Meal);
}

/// <summary>A food in the search results, Recent or Favourites: tap for the amount, "+" for the last amount used.</summary>
public partial class FoodRowViewModel(FoodInfo info, AddFoodViewModel owner) : ObservableObject
{
    public FoodInfo Info { get; } = info;
    public string Name => Info.Name;

    /// <summary>The brand or where it comes from, then kcal per serving (per 100 g without one).</summary>
    public string Subtitle => Info.NeedsEdit
        ? $"{Info.Origin} · check the numbers first"
        : $"{Info.Origin} · " + (Info.HasServing
            ? $"{CalorieGoals.K(Info.Kcal)} kcal · {Info.ServingName}"
            : $"{CalorieGoals.K((int)Math.Round(Info.KcalPer100))} kcal / {(Info.IsLiquid ? "100 ml" : "100 g")}");

    /// <summary>"Open Food Facts" under its foods.</summary>
    public string? Attribution => Info.Attribution;

    public bool IsFavourite => Info.IsFavourite;
    public bool CanQuickAdd => !Info.NeedsEdit;

    [RelayCommand] private Task Open() => owner.OpenFoodAsync(Info);
    [RelayCommand] private Task QuickAdd() => owner.QuickLogAsync(Info);
}

/// <summary>
/// The Add food sheet (Phase 26, a popup; a sheet from the bottom on phones). One view model with four pages: the search
/// (user's foods, the bundled USDA catalog, then Open Food Facts after a pause; Recent and Favourites while the box is
/// empty), the amount, the New food form and the Quick add. Opened for a meal (the time of day decides which one it starts
/// on) or for an existing entry of today to edit. Every change tells the screen under it, which reloads.
/// </summary>
public partial class AddFoodViewModel : ViewModelBase
{
    private readonly FoodService _food;
    private readonly ProfileService _profiles;
    private readonly SessionService _session;
    private readonly Action _closePopup;
    private readonly IBarcodeScanner _scanner;
    private Func<FoodResult, Task> _changed = _ => Task.CompletedTask;
    private CancellationTokenSource? _search;
    private UnitSystem _units = UnitSystem.Metric;
    private int? _entryId;
    private AddFoodPage _backTo = AddFoodPage.Search;

    /// <param name="closePopup">Closes the sheet (the shell's ClosePopup).</param>
    public AddFoodViewModel(FoodService food, ProfileService profiles, SessionService session, IBarcodeScanner scanner, Action closePopup)
    {
        _food = food;
        _profiles = profiles;
        _session = session;
        _closePopup = closePopup;
        _scanner = scanner;
        MealOptions = Enum.GetValues<Meal>().Select(m => new MealOptionViewModel(m, this)).ToList();
        PickMeal(MealDefaults.ForTime(TimeOnly.FromDateTime(AppClock.Now)));
    }

    private int? UserId => _session.CurrentUser?.Id;

    /// <summary>How long the typing has to stop before Open Food Facts is asked (500 ms; tests set it to zero).</summary>
    public TimeSpan OnlineDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>The latest search, for tests to await.</summary>
    public Task SearchTask { get; private set; } = Task.CompletedTask;

    public IReadOnlyList<MealOptionViewModel> MealOptions { get; }
    [ObservableProperty] public partial int SelectedMealIndex { get; set; }
    public Meal SelectedMeal => MealOptions[SelectedMealIndex].Meal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearchPage), nameof(IsAmountPage), nameof(IsFormPage), nameof(IsQuickPage), nameof(IsBarcodePage), nameof(Title),
        nameof(ShowMealPill), nameof(CanGoBack))]
    public partial AddFoodPage Page { get; set; }

    public bool IsSearchPage => Page == AddFoodPage.Search;
    public bool IsAmountPage => Page == AddFoodPage.Amount;
    public bool IsFormPage => Page == AddFoodPage.Form;
    public bool IsQuickPage => Page == AddFoodPage.Quick;
    public bool IsBarcodePage => Page == AddFoodPage.Barcode;
    public bool ShowMealPill => Page != AddFoodPage.Form;
    public bool CanGoBack => Page != AddFoodPage.Search && _entryId is null;

    public string Title => Page switch
    {
        AddFoodPage.Amount => _entryId is null ? "Add food" : "Edit food",
        AddFoodPage.Form => Form?.Title ?? "New food",
        AddFoodPage.Quick => _entryId is null ? "Quick add" : "Edit quick add",
        AddFoodPage.Barcode => "Scan barcode",
        _ => "Add food"
    };

    [ObservableProperty] public partial string? Notice { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }

    // ===== Search =====
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial IReadOnlyList<FoodRowViewModel> Results { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<FoodRowViewModel> Recents { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<FoodRowViewModel> Favourites { get; set; } = [];
    [ObservableProperty] public partial bool IsSearchingOnline { get; set; }
    [ObservableProperty] public partial bool IsOffline { get; set; }
    [ObservableProperty] public partial string? OnlineMessage { get; set; }

    public bool ShowSuggestions => string.IsNullOrWhiteSpace(SearchText);
    public bool ShowResults => !ShowSuggestions;
    public bool HasRecents => Recents.Count > 0;
    public bool HasFavourites => Favourites.Count > 0;
    public bool NoResults => ShowResults && Results.Count == 0 && !IsSearchingOnline && OnlineMessage is null;
    public bool ShowOfflineNote => IsOffline && ShowResults;

    // ===== Amount =====
    [ObservableProperty] public partial FoodAmountViewModel? Amount { get; set; }
    [ObservableProperty] public partial bool IsFavourite { get; set; }
    public bool IsEditingEntry => _entryId is not null;
    public bool CanEditFood => _entryId is null;
    public string LogLabel => Amount?.Kcal is { } k
        ? $"{(_entryId is null ? "Log" : "Save")} {CalorieGoals.K(k)} kcal"
        : (_entryId is null ? "Log" : "Save");

    // ===== New food form =====
    [ObservableProperty] public partial FoodFormViewModel? Form { get; set; }

    // ===== Barcode (Phase 26d) =====

    /// <summary>The camera button shows only where the head can scan (the phones); typing a number works everywhere.</summary>
    public bool CanScan => _scanner.IsSupported;

    [ObservableProperty] public partial string BarcodeText { get; set; } = "";
    [ObservableProperty] public partial string? BarcodeError { get; set; }
    [ObservableProperty] public partial bool IsLookingUp { get; set; }

    /// <summary>Set when a barcode isn't in Open Food Facts or the user's foods: "We couldn't find this barcode."</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnknownBarcode))]
    public partial string? UnknownBarcode { get; set; }

    public bool HasUnknownBarcode => UnknownBarcode is not null;

    // ===== Quick add =====
    [ObservableProperty] public partial string QuickKcalText { get; set; } = "";
    [ObservableProperty] public partial string QuickLabel { get; set; } = "";
    [ObservableProperty] public partial string? QuickError { get; set; }

    // ===== Opening =====

    /// <summary>A new entry. <paramref name="meal"/> null: the meal for the time of day.</summary>
    public void Load(Meal? meal, Func<FoodResult, Task> changed)
    {
        _changed = changed;
        _entryId = null;
        PickMeal(meal ?? MealDefaults.ForTime(TimeOnly.FromDateTime(AppClock.Now)));
        Page = AddFoodPage.Search;
        _ = LoadAsync();
    }

    /// <summary>Edits one of today's entries: a food entry opens at its amount, a Quick add at its calories.</summary>
    public async Task LoadEntryAsync(FoodEntryInfo entry, Func<FoodResult, Task> changed)
    {
        _changed = changed;
        _entryId = entry.Id;
        PickMeal(entry.Meal);
        OnPropertyChanged(nameof(IsEditingEntry));
        OnPropertyChanged(nameof(CanEditFood));
        await LoadUnitsAsync();

        if (entry.IsQuickAdd)
        {
            QuickKcalText = entry.Kcal.ToString(CultureInfo.InvariantCulture);
            QuickLabel = entry.Name == "Quick add" ? "" : entry.Name;
            Page = AddFoodPage.Quick;
            return;
        }

        var info = entry.FoodId is { } id && UserId is { } me ? await _food.GetFoodAsync(me, id) : null;
        info ??= FromSnapshot(entry);   // the food is gone: the entry's own numbers, per 100 g
        IsFavourite = info.IsFavourite;
        Amount = new FoodAmountViewModel(info, _units, entry.Servings, entry.Grams);
        WatchAmount();
        Page = AddFoodPage.Amount;
    }

    private static FoodInfo FromSnapshot(FoodEntryInfo e)
    {
        var grams = e.Grams is > 0 ? e.Grams.Value : 100;
        double Per100(double v) => Math.Round(v * 100 / grams, 2);
        return new FoodInfo(null, FoodSource.Mine, null, e.Name, e.Brand, e.IsLiquid, Math.Round(e.Kcal * 100.0 / grams, 1),
            e.Protein is { } p ? Per100(p) : null, e.Carbs is { } c ? Per100(c) : null, e.Fat is { } f ? Per100(f) : null,
            null, null);
    }

    private async Task LoadAsync()
    {
        await LoadUnitsAsync();
        await LoadSuggestionsAsync();
    }

    private async Task LoadUnitsAsync()
    {
        try
        {
            if (UserId is { } me && await _profiles.GetAsync(me) is { } s) _units = s.Profile.Units;
        }
        catch (Exception) { /* metric */ }
    }

    private async Task LoadSuggestionsAsync()
    {
        if (UserId is not { } me) return;
        try
        {
            Recents = (await _food.RecentsAsync(me)).Select(f => new FoodRowViewModel(f, this)).ToList();
            Favourites = (await _food.FavouritesAsync(me)).Select(f => new FoodRowViewModel(f, this)).ToList();
        }
        catch (Exception) { Error = "Could not load your recent foods."; }
        OnPropertyChanged(nameof(HasRecents));
        OnPropertyChanged(nameof(HasFavourites));
    }

    public void PickMeal(Meal meal)
    {
        for (var i = 0; i < MealOptions.Count; i++) MealOptions[i].IsSelected = MealOptions[i].Meal == meal;
        SelectedMealIndex = MealOptions.ToList().FindIndex(o => o.Meal == meal);
        OnPropertyChanged(nameof(SelectedMeal));
    }

    // ===== Search =====

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(ShowSuggestions));
        OnPropertyChanged(nameof(ShowResults));
        SearchTask = RunSearchAsync(value);
    }

    partial void OnResultsChanged(IReadOnlyList<FoodRowViewModel> value) => OnPropertyChanged(nameof(NoResults));
    partial void OnIsSearchingOnlineChanged(bool value) => OnPropertyChanged(nameof(NoResults));
    partial void OnOnlineMessageChanged(string? value) => OnPropertyChanged(nameof(NoResults));
    partial void OnIsOfflineChanged(bool value) => OnPropertyChanged(nameof(ShowOfflineNote));

    private async Task RunSearchAsync(string raw)
    {
        _search?.Cancel();
        var cts = _search = new CancellationTokenSource();
        var ct = cts.Token;
        var text = (raw ?? "").Trim();
        IsOffline = false;
        OnlineMessage = null;
        IsSearchingOnline = false;
        Error = null;

        if (UserId is not { } me) return;
        try
        {
            if (text.Length == 0)
            {
                Results = [];
                await LoadSuggestionsAsync();
                return;
            }

            // A typed barcode looks that product up (the scanner comes in 26d)
            if (OpenFoodFactsClient.IsBarcode(text))
            {
                IsSearchingOnline = true;
                var r = await _food.LookupBarcodeAsync(me, text, Language(), ct);
                if (ct.IsCancellationRequested) return;
                Results = r.Food is { } f ? [new FoodRowViewModel(f, this)] : [];
                OnlineMessage = r.Status switch
                {
                    LookupStatus.NotFound => "We couldn't find this barcode.",
                    LookupStatus.Offline => "Offline: can't look up barcodes right now.",
                    LookupStatus.Failed => r.Error ?? "Open Food Facts could not answer right now.",
                    _ => null
                };
                IsOffline = r.Status == LookupStatus.Offline;
                return;
            }

            var local = await _food.SearchLocalAsync(me, text);
            if (ct.IsCancellationRequested) return;
            var rows = local.Select(f => new FoodRowViewModel(f, this)).ToList();
            Results = rows;
            if (text.Length < OpenFoodFactsClient.MinSearchLength) return;

            await Task.Delay(OnlineDelay, ct);
            IsSearchingOnline = true;
            var online = await _food.SearchOnlineAsync(me, text, Language(), ct);
            if (ct.IsCancellationRequested) return;

            var seen = new HashSet<(FoodSource, string?)>(local.Select(f => (f.Source, f.SourceKey)));
            var merged = new List<FoodRowViewModel>(rows);
            foreach (var f in online.Foods)
                if (f.SourceKey is null || seen.Add((f.Source, f.SourceKey))) merged.Add(new FoodRowViewModel(f, this));
            Results = merged;
            IsOffline = online.Offline;
            OnlineMessage = online.Success ? null : online.Offline ? null : online.Error;
        }
        catch (OperationCanceledException) { /* typed on */ }
        catch (Exception)
        {
            if (!ct.IsCancellationRequested) Error = "Could not search your foods.";
        }
        finally
        {
            if (ReferenceEquals(_search, cts)) IsSearchingOnline = false;
        }
    }

    private static string? Language() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName is { Length: 2 } l ? l : null;

    // ===== Choosing and logging =====

    internal async Task OpenFoodAsync(FoodInfo info)
    {
        Error = null;
        Notice = null;
        if (info.NeedsEdit)
        {
            OpenForm(new FoodFormViewModel(info, "Check this food", info.Problem));
            return;
        }
        IsFavourite = info.IsFavourite;
        Amount = new FoodAmountViewModel(info, _units);
        WatchAmount();
        _backTo = AddFoodPage.Search;
        Page = AddFoodPage.Amount;
        await Task.CompletedTask;
    }

    private void WatchAmount()
    {
        if (Amount is null) return;
        Amount.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FoodAmountViewModel.KcalText) or nameof(FoodAmountViewModel.Kcal)) OnPropertyChanged(nameof(LogLabel));
        };
        OnPropertyChanged(nameof(LogLabel));
        OnPropertyChanged(nameof(IsEditingEntry));
        OnPropertyChanged(nameof(CanEditFood));
    }

    /// <summary>The "+" on a row: logs the amount last used for that food, in the meal the sheet is on, and stays open.</summary>
    internal async Task QuickLogAsync(FoodInfo info)
    {
        if (UserId is not { } me) return;
        Error = null;
        Notice = null;
        try
        {
            var r = await _food.QuickLogAsync(me, info, SelectedMeal);
            if (!r.Success) { Error = r.Error; return; }
            Notice = $"Added {info.Name} · {CalorieGoals.K(r.Entry?.Kcal ?? 0)} kcal";
            await _changed(r);
            if (SearchText.Trim().Length == 0) await LoadSuggestionsAsync();
        }
        catch (Exception) { Error = "Could not save your food."; }
    }

    [RelayCommand]
    private async Task LogAmountAsync()
    {
        try
        {
            if (UserId is not { } me || Amount is null) return;
            Error = null;
            if (Amount.ToAmount() is not { } a) { Error = Amount.Error ?? "Enter an amount first."; return; }

            var r = _entryId is { } id
                ? await _food.EditEntryAsync(me, id, SelectedMeal, a.Amount, a.Unit)
                : await _food.LogAsync(me, Amount.Food, SelectedMeal, a.Amount, a.Unit);
            if (!r.Success) { Error = r.Error; return; }
            await _changed(r);
            _closePopup();
        }
        catch (Exception) { Error = "Could not save your food."; }
    }

    [RelayCommand]
    private async Task DeleteEntryAsync()
    {
        try
        {
            if (UserId is not { } me || _entryId is not { } id) return;
            var r = await _food.DeleteEntryAsync(me, id);
            if (!r.Success) { Error = r.Error; return; }
            await _changed(r);
            _closePopup();
        }
        catch (Exception) { Error = "Could not delete that."; }
    }

    [RelayCommand]
    private async Task ToggleFavouriteAsync()
    {
        try
        {
            if (UserId is not { } me || Amount is null) return;
            var r = await _food.SetFavouriteAsync(me, Amount.Food, !IsFavourite);
            if (!r.Success) { Error = r.Error; return; }
            IsFavourite = r.Food!.IsFavourite;
            // Later saves must point to the saved row (the star was the first save)
            Amount = new FoodAmountViewModel(r.Food, _units, Amount.ToAmount() is { Unit: AmountUnit.Servings } s ? s.Amount : null,
                Amount.Grams);
            WatchAmount();
        }
        catch (Exception) { Error = "Could not save that."; }
    }

    // ===== Form =====

    [RelayCommand]
    private void NewFood()
    {
        Error = null;
        Notice = null;
        var typed = SearchText.Trim();
        var barcode = OpenFoodFactsClient.IsBarcode(typed) ? typed : null;   // "Add it as my food" under the typed barcode
        var form = new FoodFormViewModel(null, "New food") { Barcode = barcode };
        if (barcode is null && typed.Length > 0) form.Name = typed;
        OpenForm(form);
    }

    [RelayCommand]
    private void EditFood()
    {
        if (Amount is null) return;
        OpenForm(new FoodFormViewModel(Amount.Food, Amount.Food.Source == FoodSource.Mine ? "Edit food" : "Edit as your own food"));
    }

    private void OpenForm(FoodFormViewModel form)
    {
        _backTo = Page;
        Form = form;
        Page = AddFoodPage.Form;
    }

    [RelayCommand]
    private async Task SaveFormAsync()
    {
        try
        {
            if (UserId is not { } me || Form is null) return;
            if (Form.TryBuild() is not { } input) return;

            var r = Form.Original is { } original
                ? await _food.SaveFoodEditAsync(me, original, input)
                : await _food.CreateFoodAsync(me, input);
            if (!r.Success) { Form.Error = r.Error; return; }

            Form = null;
            await OpenFoodAsync(r.Food!);
        }
        catch (Exception) { if (Form is not null) Form.Error = "Could not save that food."; }
    }

    // ===== Quick add =====

    [RelayCommand]
    private void ShowQuick()
    {
        Error = null;
        Notice = null;
        QuickError = null;
        QuickKcalText = "";
        QuickLabel = "";
        _backTo = Page;
        Page = AddFoodPage.Quick;
    }

    [RelayCommand]
    private async Task SaveQuickAsync()
    {
        try
        {
            if (UserId is not { } me) return;
            QuickError = null;
            if (!NumberParsing.TryParseWhole(QuickKcalText, out var kcal))
            {
                QuickError = "Enter the calories as a whole number, e.g. 450.";
                return;
            }
            var r = _entryId is { } id
                ? await _food.EditQuickAddAsync(me, id, SelectedMeal, kcal, QuickLabel)
                : await _food.QuickAddAsync(me, kcal, QuickLabel, SelectedMeal);
            if (!r.Success) { QuickError = r.Error; return; }
            await _changed(r);
            _closePopup();
        }
        catch (Exception) { QuickError = "Could not save your calories."; }
    }


    // ===== Barcode =====

    /// <summary>The camera button: scans, then looks the code up. Without a scanner it opens the page to type the number.</summary>
    [RelayCommand]
    private async Task ScanAsync()
    {
        try
        {
            Error = null;
            Notice = null;
            if (!_scanner.IsSupported) { ShowBarcode(); return; }

            var code = await _scanner.ScanAsync();
            if (code is null)
            {
                // Cancelled: stay where we were. Refused or no camera: say why, with the box to type the number instead
                if (_scanner.Problem is { } problem)
                {
                    ResetBarcode();
                    BarcodeError = problem;
                    Page = AddFoodPage.Barcode;
                }
                return;
            }
            BarcodeText = code.Trim();
            await LookUpCodeAsync(BarcodeText);
        }
        catch (Exception) { Error = "Could not scan that."; }
    }

    /// <summary>"Enter number": the barcode page with its box.</summary>
    [RelayCommand]
    private void ShowBarcode()
    {
        Error = null;
        Notice = null;
        ResetBarcode();
        if (Page != AddFoodPage.Barcode) BarcodeText = "";   // A fresh visit starts empty; a failed lookup keeps its number
        _backTo = Page == AddFoodPage.Barcode ? AddFoodPage.Search : Page;
        Page = AddFoodPage.Barcode;
    }

    private void ResetBarcode()
    {
        BarcodeError = null;
        UnknownBarcode = null;
        IsLookingUp = false;
    }

    [RelayCommand]
    private async Task LookUpNumberAsync()
    {
        try
        {
            var text = BarcodeText.Trim();
            if (!OpenFoodFactsClient.IsBarcode(text))
            {
                UnknownBarcode = null;
                BarcodeError = "Enter the 8 to 14 digits printed under the barcode.";
                return;
            }
            await LookUpCodeAsync(text);
        }
        catch (Exception) { BarcodeError = "Could not look that up."; }
    }

    /// <summary>The user's own food with that barcode first, then Open Food Facts. A product opens at its amount (or its
    /// form, when its numbers look wrong); anything else stays on the barcode page and says why.</summary>
    private async Task LookUpCodeAsync(string code)
    {
        if (UserId is not { } me) return;
        ResetBarcode();
        if (!OpenFoodFactsClient.IsBarcode(code))
        {
            BarcodeError = "That doesn't look like a food barcode.";
            Page = AddFoodPage.Barcode;
            return;
        }

        IsLookingUp = true;
        Page = AddFoodPage.Barcode;
        BarcodeText = code;
        try
        {
            var r = await _food.LookupBarcodeAsync(me, code, Language());
            switch (r.Status)
            {
                case LookupStatus.Found when r.Food is { } food:
                    IsLookingUp = false;
                    await OpenFoodAsync(food);   // Back from its amount returns to the search
                    break;
                case LookupStatus.NotFound:
                    UnknownBarcode = code;
                    break;
                case LookupStatus.Offline:
                    BarcodeError = "Offline: can't look up barcodes right now. Check your connection and try again, or add the food yourself.";
                    break;
                default:
                    BarcodeError = r.Error ?? "Open Food Facts could not answer right now. Try again in a minute.";
                    break;
            }
        }
        finally { IsLookingUp = false; }
    }

    /// <summary>After "We couldn't find this barcode": a food of your own that the next scan finds.</summary>
    [RelayCommand]
    private void AddAsMyFood()
    {
        if (UnknownBarcode is not { } code) return;
        OpenForm(new FoodFormViewModel(null, "New food") { Barcode = code });
    }

    /// <summary>After "We couldn't find this barcode": back to the search box.</summary>
    [RelayCommand]
    private void SearchByName()
    {
        ResetBarcode();
        BarcodeText = "";
        SearchText = "";
        Page = AddFoodPage.Search;
    }

    // ===== Navigation =====

    [RelayCommand]
    private void Back()
    {
        Error = null;
        Form = null;
        Page = _backTo == AddFoodPage.Form || _backTo == AddFoodPage.Quick ? AddFoodPage.Search : _backTo;
        if (Page == AddFoodPage.Search) ResetBarcode();
    }

    [RelayCommand]
    private void Close() => _closePopup();
}
