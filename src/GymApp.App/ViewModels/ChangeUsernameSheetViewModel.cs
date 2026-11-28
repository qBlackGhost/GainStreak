using System;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Core;
using GymApp.Services;

namespace GymApp.App.ViewModels;

/// <summary>
/// The Change username sheet (Phase 28, phones; a sheet from the bottom, opened from Settings' Username row). The new
/// name gets live feedback from <see cref="Credentials.CheckUsername"/> (nothing is sent until Change); the server has
/// the last word on taken names, the password and the 30-day wait. A change of capital letters only is always allowed.
/// </summary>
public partial class ChangeUsernameSheetViewModel : ViewModelBase
{
    public const string RulesHint = "Letters, digits, _ . -";
    public const string CapitalsOnlyHint = "No waiting time for capital letters";
    public const string SameNameHint = "That's your username now.";
    public const string TesterText = "Friends see your new name straight away. Your invite link and QR code keep working. " +
                                     "You'll sign in with the new name. You can change it again after 30 days.";

    private readonly IAccountService _accounts;
    private readonly Action _close;
    private readonly Action<string> _renamed;

    public ChangeUsernameSheetViewModel(IAccountService accounts, string currentUsername, Action close, Action<string> renamed)
    {
        _accounts = accounts;
        _close = close;
        _renamed = renamed;
        CurrentUsername = currentUsername;
        AllowedAt = accounts.UsernameChangeAllowedAt;
        Refresh();
    }

    public string CurrentUsername { get; }

    /// <summary>Show password, as on Log in.</summary>
    public PasswordVisibility PasswordView { get; } = new();

    [ObservableProperty] public partial string NewUsername { get; set; } = "";
    [ObservableProperty] public partial string Password { get; set; } = "";

    /// <summary>The line under the new name: the rules, the wait, or what's wrong with it.</summary>
    [ObservableProperty] public partial string Hint { get; private set; } = RulesHint;
    /// <summary>The name passes the rules (the hint gets a check and turns lime).</summary>
    [ObservableProperty] public partial bool IsHintOk { get; private set; }
    /// <summary>The hint is a problem with the name (shown red, as the account forms' errors).</summary>
    [ObservableProperty] public partial bool IsHintError { get; private set; }
    [ObservableProperty] public partial string CounterText { get; private set; } = "";

    /// <summary>The server's refusal of the name (taken, filtered, too soon); cleared by typing.</summary>
    [ObservableProperty] public partial string? UsernameError { get; private set; }
    [ObservableProperty] public partial string? PasswordError { get; private set; }
    /// <summary>A refusal about neither box.</summary>
    [ObservableProperty] public partial string? Error { get; private set; }

    [ObservableProperty] public partial bool IsOffline { get; private set; }
    /// <summary>When the next rename works (UTC), while it has to wait.</summary>
    [ObservableProperty] public partial DateTime? AllowedAt { get; private set; }
    [ObservableProperty] public partial bool IsSaving { get; private set; }

    public string? WaitNotice => AllowedAt is { } at ? Credentials.UsernameChangeTooSoonMessage(at) : null;
    public bool IsWaiting => AllowedAt is not null;
    public string ChangeLabel => IsSaving ? "Changing…" : "Change";
    public bool CanChange { get; private set; }

    public bool ShowHint => UsernameError is null;
    public bool HasUsernameError => UsernameError is not null;

    partial void OnNewUsernameChanged(string value)
    {
        UsernameError = null;
        Error = null;
        Refresh();
    }

    partial void OnPasswordChanged(string value)
    {
        PasswordError = null;
        Error = null;
        Refresh();
    }

    partial void OnUsernameErrorChanged(string? value)
    {
        OnPropertyChanged(nameof(ShowHint));
        OnPropertyChanged(nameof(HasUsernameError));
    }

    partial void OnAllowedAtChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(WaitNotice));
        OnPropertyChanged(nameof(IsWaiting));
        Refresh();
    }

    partial void OnIsOfflineChanged(bool value) => Refresh();

    partial void OnIsSavingChanged(bool value)
    {
        OnPropertyChanged(nameof(ChangeLabel));
        Refresh();
    }

    /// <summary>When the sheet opens: is the server there, and is there a wait? Until it answers, the last known wait shows.</summary>
    public async Task LoadAsync()
    {
        var check = await _accounts.CheckUsernameChangeAsync();
        IsOffline = !check.Online;
        AllowedAt = check.AllowedAt;
    }

    private void Refresh()
    {
        var name = NewUsername.Trim();
        CounterText = $"{name.Length.ToString(CultureInfo.InvariantCulture)} / {Credentials.MaxUsernameLength.ToString(CultureInfo.InvariantCulture)}";
        var capitalsOnly = name.Length > 0 && name != CurrentUsername
                           && string.Equals(name, CurrentUsername, StringComparison.OrdinalIgnoreCase);
        var problem = name.Length == 0 || name == CurrentUsername ? null : Credentials.CheckUsername(name);
        var waiting = AllowedAt is { } at && at > DateTime.UtcNow.Add(AppClock.Offset) && !capitalsOnly;

        (Hint, IsHintOk, IsHintError) = true switch
        {
            _ when name.Length == 0 => (RulesHint, false, false),
            _ when name == CurrentUsername => (SameNameHint, false, false),
            _ when problem is not null => (problem, false, true),
            _ when capitalsOnly => (CapitalsOnlyHint, true, false),
            _ when waiting => (WaitNotice!, false, false),
            _ => (RulesHint, true, false)
        };

        CanChange = IsHintOk && Password.Length > 0 && !IsOffline && !IsSaving;
        OnPropertyChanged(nameof(CanChange));
        ChangeCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private async Task ChangeAsync()
    {
        IsSaving = true;
        try
        {
            var r = await _accounts.ChangeUsernameAsync(NewUsername, Password);
            if (r.Success)
            {
                _close();
                _renamed(r.User!.Username);
                return;
            }
            AllowedAt = _accounts.UsernameChangeAllowedAt;
            if (r.Error == AccountService.RenameNeedsConnectionMessage) IsOffline = true;
            else if (r.Field == FormField.Username) UsernameError = r.Error;
            else if (r.Field == FormField.Password) PasswordError = r.Error;
            else Error = r.Error;
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand] private void Close() => _close();
}
