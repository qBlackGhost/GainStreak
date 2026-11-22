using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Core;

namespace GymApp.App.ViewModels;

/// <summary>The phones' Profile pages (0.23.1). Main holds you (stats, weight, body data and vacation as rows);
/// the gear opens Settings, a list whose rows open the other pages. On wider screens there are no pages: every card
/// shows, as before.</summary>
public enum ProfilePage
{
    Main, Settings, BodyData, Vacation, Account, DeleteAccount, Notifications, Health, HomeCity, Appearance,
}

public partial class ProfileViewModel : IClosesOnBack
{
    /// <summary>Laid out for a phone (ProfileView sets it from MainView's "phone" class).</summary>
    [ObservableProperty] public partial bool IsPhone { get; set; }

    [ObservableProperty] public partial ProfilePage Page { get; set; } = ProfilePage.Main;

    partial void OnIsPhoneChanged(bool value) => RaisePageProperties();

    partial void OnPageChanged(ProfilePage oldValue, ProfilePage newValue)
    {
        // A page left halfway: its open forms close (typed passwords aren't kept around)
        if (oldValue == ProfilePage.Account)
        {
            if (IsEditingEmail) CancelEmailCommand.Execute(null);
            if (IsChangingPassword) CancelPasswordCommand.Execute(null);
        }
        if (oldValue == ProfilePage.DeleteAccount && IsConfirmingDelete) CancelDeleteCommand.Execute(null);
        if (newValue == ProfilePage.DeleteAccount && !IsConfirmingDelete) AskDeleteCommand.Execute(null);
        Error = null;
        Info = null;
        RaisePageProperties();
    }

    // The Delete account page is only its confirmation: Cancel goes back to Settings
    partial void OnIsConfirmingDeleteChanged(bool value)
    {
        if (!value && IsPhone && Page == ProfilePage.DeleteAccount) Page = ProfilePage.Settings;
    }

    private static readonly string[] PageProperties =
    [
        nameof(IsMainPage), nameof(IsSettingsPage), nameof(IsSubPage), nameof(PageTitle), nameof(ShowsCards),
        nameof(ShowsBodyData), nameof(ShowsWeight), nameof(ShowsHomeCity), nameof(ShowsAccountCard),
        nameof(ShowsAccountDetails), nameof(ShowsAccountDelete), nameof(ShowsVacation), nameof(ShowsNotifications),
        nameof(ShowsHealth), nameof(ShowsAppearance), nameof(ShowsBadges),
    ];

    private void RaisePageProperties()
    {
        foreach (var p in PageProperties) OnPropertyChanged(p);
    }

    public bool IsMainPage => Page == ProfilePage.Main;
    public bool IsSettingsPage => Page == ProfilePage.Settings;
    /// <summary>Any page but Main: it gets the back row with its title.</summary>
    public bool IsSubPage => Page != ProfilePage.Main;

    public string PageTitle => Page switch
    {
        ProfilePage.Settings => "Settings",
        ProfilePage.BodyData => "Body data",
        ProfilePage.Vacation => "Vacation",
        ProfilePage.Account => "Account",
        ProfilePage.DeleteAccount => "Delete account",
        ProfilePage.Notifications => "Notifications",
        ProfilePage.Health => Health.Texts.Title,
        ProfilePage.HomeCity => "Home city",
        ProfilePage.Appearance => "Appearance",
        _ => "Profile",
    };

    private bool Shows(ProfilePage page) => !IsPhone || Page == page;

    /// <summary>The cards' grid: everywhere but the phones' Settings list.</summary>
    public bool ShowsCards => !IsPhone || Page != ProfilePage.Settings;
    public bool ShowsBodyData => Shows(ProfilePage.BodyData);
    /// <summary>Main, and the Body data page too: the setup card and Calories send people there to add their weight,
    /// and the maintenance estimate needs it.</summary>
    public bool ShowsWeight => Shows(ProfilePage.Main) || Shows(ProfilePage.BodyData);
    public bool ShowsHomeCity => Shows(ProfilePage.HomeCity);
    public bool ShowsAccountDetails => Shows(ProfilePage.Account);
    public bool ShowsAccountDelete => Shows(ProfilePage.DeleteAccount);
    public bool ShowsAccountCard => ShowsAccountDetails || ShowsAccountDelete;
    public bool ShowsVacation => Shows(ProfilePage.Vacation);
    public bool ShowsNotifications => HasNotifications && Shows(ProfilePage.Notifications);
    public bool ShowsHealth => Health.IsSupported && Shows(ProfilePage.Health);
    public bool ShowsAppearance => Shows(ProfilePage.Appearance);

    [RelayCommand] private void OpenPage(ProfilePage page) => Page = page;

    /// <summary>Settings' Password row: the Account page with the password form open.</summary>
    [RelayCommand]
    private void OpenPassword()
    {
        Page = ProfilePage.Account;
        if (!IsChangingPassword) ChangePasswordCommand.Execute(null);
    }

    [RelayCommand] private void PageBack() => CloseOnBack();

    /// <summary>Back (the page's arrow, Android's back): a settings page goes to Settings, Settings and the Main
    /// rows' pages to Main. On Main, the shell goes back as usual.</summary>
    public bool CloseOnBack()
    {
        if (Page == ProfilePage.Main) return false;
        Page = Page is ProfilePage.Settings or ProfilePage.BodyData or ProfilePage.Vacation
            ? ProfilePage.Main : ProfilePage.Settings;
        return true;
    }

    // ===== The rows' summaries =====

    /// <summary>Main's Body data row: "Male · Age 28 · 180 cm · Active".</summary>
    public string BodySummary
    {
        get
        {
            var parts = new List<string>();
            if (SelectedSex is { } sex) parts.Add(sex == Sex.Male ? "Male" : "Female");
            if (AgeNumber != "—") parts.Add(AgeText);
            if (IsMetric && HeightCmText.Trim() is { Length: > 0 } cm) parts.Add($"{cm} cm");
            if (IsImperial && HeightFtText.Trim() is { Length: > 0 } ft)
                parts.Add(HeightInText.Trim() is { Length: > 0 } inch ? $"{ft} ft {inch} in" : $"{ft} ft");
            if (SelectedActivity is { } a) parts.Add(a.ShortName);
            return parts.Count == 0 ? "Not filled in yet" : string.Join(" · ", parts);
        }
    }

    /// <summary>Under it: the maintenance estimate, or why there's none, or that edits wait to be saved.</summary>
    public string BodySummaryDetail => IsDirty ? "Unsaved changes"
        : MaintenanceNote ?? $"{MaintenanceText} kcal / day maintenance";

    private static readonly HashSet<string> BodySummaryInputs =
    [
        nameof(SelectedSex), nameof(BirthDayText), nameof(BirthMonth), nameof(BirthYearText), nameof(HeightCmText),
        nameof(HeightFtText), nameof(HeightInText), nameof(SelectedActivity), nameof(SelectedUnits), nameof(IsDirty),
        nameof(MaintenanceText), nameof(MaintenanceNote),
    ];

    private void RaiseSummaries(string? changed)
    {
        if (changed is null) return;
        if (BodySummaryInputs.Contains(changed))
        {
            OnPropertyChanged(nameof(BodySummary));
            OnPropertyChanged(nameof(BodySummaryDetail));
        }
        if (changed is nameof(ThemeIndex)) OnPropertyChanged(nameof(ThemeSummary));
        if (changed is nameof(HasHomeCity) or nameof(HomeCityName)) OnPropertyChanged(nameof(HomeCitySummary));
    }

    public string ThemeSummary => ThemeIndex switch { 1 => "Light", 2 => "Dark", _ => "System" };
    public string HomeCitySummary => HasHomeCity ? HomeCityName : "Not set";

    public string NotificationsSummary =>
        (Reminders.IsSupported && Reminders.IsOn) || (FriendNotifications.IsSupported && FriendNotifications.IsOn) ? "On" : "Off";

    /// <summary>Health's row: a question waiting or a problem shows here, since the card is a page away.</summary>
    public string HealthSummary =>
        !string.IsNullOrEmpty(Health.OfferText) || !string.IsNullOrEmpty(Health.StepsText) ? "Needs an answer"
        : !string.IsNullOrEmpty(Health.Problem) ? "Needs attention"
        : Health.IsOn ? "On" : "Off";

    public bool HealthNeedsLook =>
        !string.IsNullOrEmpty(Health.OfferText) || !string.IsNullOrEmpty(Health.StepsText) || !string.IsNullOrEmpty(Health.Problem);

    /// <summary>The child cards' changes, for the Settings rows. They live as long as this view model.</summary>
    private void WatchSettingsRows()
    {
        Reminders.PropertyChanged += OnNotificationsChanged;
        FriendNotifications.PropertyChanged += OnNotificationsChanged;
        Health.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HealthSummary));
            OnPropertyChanged(nameof(HealthNeedsLook));
        };
    }

    private void OnNotificationsChanged(object? sender, PropertyChangedEventArgs e) =>
        OnPropertyChanged(nameof(NotificationsSummary));
}
