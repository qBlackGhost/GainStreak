using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Services;

namespace GymApp.App.ViewModels;

/// <summary>Sign in, staying signed in until logging out. A login that matches an account on this computer from before
/// the server existed turns into the "Move this account online" step.</summary>
public partial class LoginViewModel : ViewModelBase
{
    private readonly IAccountService _accounts;
    private readonly Func<MainViewModel> _shell;
    private readonly ErrorLog _log;
    private MoveOnlineOffer? _offer;

    public LoginViewModel(IAccountService accounts, SessionService session, Func<MainViewModel> shell, ErrorLog log)
    {
        _accounts = accounts; _shell = shell; _log = log;
        Notice = session.SignOutNotice;
    }

    /// <summary>The username or the account email.</summary>
    public PasswordVisibility PasswordView { get; } = new();

    [ObservableProperty] public partial string Username { get; set; } = "";
    [ObservableProperty] public partial string Password { get; set; } = "";
    [ObservableProperty] public partial string? Error { get; set; }

    /// <summary>Why the app signed out on its own, e.g. "Your session ended. Sign in again."</summary>
    [ObservableProperty] public partial string? Notice { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand), nameof(MoveOnlineCommand))]
    public partial bool IsBusy { get; set; }

    // ===== Move this account online =====
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSigningIn))]
    public partial bool IsMovingOnline { get; set; }
    public bool IsSigningIn => !IsMovingOnline;

    [ObservableProperty] public partial string MoveOnlineText { get; set; } = "";
    [ObservableProperty] public partial bool ShowMoveEmail { get; set; }
    [ObservableProperty] public partial bool ShowMoveUsername { get; set; }
    [ObservableProperty] public partial string MoveEmail { get; set; } = "";
    [ObservableProperty] public partial string MoveUsername { get; set; } = "";
    [ObservableProperty] public partial string? MoveEmailError { get; set; }
    [ObservableProperty] public partial string? MoveUsernameError { get; set; }

    private bool CanAct() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task LoginAsync()
    {
        Error = Notice = null;
        IsBusy = true;
        try
        {
            var result = await _accounts.SignInAsync(Username, Password);
            if (result.Success) return;   // SignIn navigates
            if (result.MoveOnline is { } offer) { StartMoveOnline(offer); return; }
            Error = result.Error;
        }
        catch (Exception e)
        {
            _log.Write(e, "Sign in");   // the message can't say what went wrong; Feedback's report can
            Error = "Could not sign in. Try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void StartMoveOnline(MoveOnlineOffer offer)
    {
        _offer = offer;
        MoveOnlineText = $"{offer.Username} is an account on this computer from before accounts went online. Move it online " +
                         "to add friends and join the leaderboard. Your workouts, points and streak stay as they are.";
        ShowMoveEmail = offer.NeedsEmail;
        ShowMoveUsername = false;
        MoveEmail = offer.Email ?? "";
        MoveUsername = offer.Username;
        MoveEmailError = MoveUsernameError = null;
        IsMovingOnline = true;
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task MoveOnlineAsync()
    {
        if (_offer is not { } offer) return;
        Error = MoveEmailError = MoveUsernameError = null;
        IsBusy = true;
        try
        {
            var result = await _accounts.MoveOnlineAsync(offer.LocalUserId, Password,
                ShowMoveEmail ? MoveEmail : null, ShowMoveUsername ? MoveUsername : null);
            if (result.Success) return;
            switch (result.Field)
            {
                // Taken on the server: ask for another one in a box of its own
                case FormField.Username: ShowMoveUsername = true; MoveUsernameError = result.Error; break;
                case FormField.Email: ShowMoveEmail = true; MoveEmailError = result.Error; break;
                default: Error = result.Error; break;
            }
        }
        catch (Exception e)
        {
            _log.Write(e, "Move online");
            Error = "Could not move your account online. Try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelMoveOnline()
    {
        _offer = null;
        IsMovingOnline = false;
        Error = null;
        Password = "";
    }

    [RelayCommand]
    private void GoToRegister() => _shell().ShowRegister();

    [RelayCommand]
    private void GoToForgotPassword() => _shell().ShowForgotPassword(Username.Contains('@') ? Username : null);
}
