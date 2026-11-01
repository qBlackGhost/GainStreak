using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Services;

namespace GymApp.App.ViewModels;

public partial class RegisterViewModel : ViewModelBase
{
    private readonly IAccountService _accounts;
    private readonly Func<MainViewModel> _shell;
    private readonly ErrorLog _log;

    public RegisterViewModel(IAccountService accounts, Func<MainViewModel> shell, ErrorLog log)
    {
        _accounts = accounts; _shell = shell; _log = log;
    }

    public PasswordVisibility PasswordView { get; } = new();

    /// <summary>The server is being asked; the button waits.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty] public partial string Username { get; set; } = "";
    [ObservableProperty] public partial string Email { get; set; } = "";
    [ObservableProperty] public partial string Password { get; set; } = "";
    [ObservableProperty] public partial string ConfirmPassword { get; set; } = "";
    [ObservableProperty] public partial bool AgreedToTerms { get; set; }
    [ObservableProperty] public partial string? TermsError { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial string? UsernameError { get; set; }
    [ObservableProperty] public partial string? EmailError { get; set; }
    [ObservableProperty] public partial string? PasswordError { get; set; }
    [ObservableProperty] public partial string? ConfirmPasswordError { get; set; }

    partial void OnAgreedToTermsChanged(bool value)
    {
        if (value) TermsError = null;
    }

    private bool CanRegister() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRegister))]
    private async Task RegisterAsync()
    {
        Error = UsernameError = EmailError = PasswordError = ConfirmPasswordError = TermsError = null;
        if (Password != ConfirmPassword) { ConfirmPasswordError = "Passwords do not match."; return; }
        if (!AgreedToTerms) { TermsError = "Agree to the Terms of Use to create an account."; return; }

        IsBusy = true;
        try
        {
            var result = await _accounts.RegisterAsync(Username, Email, Password);
            if (result.Success) return;   // signed in straight after registering, which navigates
            switch (result.Field)
            {
                case FormField.Username: UsernameError = result.Error; break;
                case FormField.Email: EmailError = result.Error; break;
                case FormField.Password: PasswordError = result.Error; break;
                default: Error = result.Error; break;
            }
        }
        catch (Exception e)
        {
            _log.Write(e, "Register");   // the message can't say what went wrong; Feedback's report can
            Error = "Could not create your account. Try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void GoToLogin() => _shell().ShowLogin();
}