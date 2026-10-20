using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Services;

namespace GymApp.Desktop.ViewModels;

public partial class RegisterViewModel : ViewModelBase
{
    private readonly IAccountService _accounts;
    private readonly Func<MainViewModel> _shell;

    public RegisterViewModel(IAccountService accounts, Func<MainViewModel> shell)
    {
        _accounts = accounts; _shell = shell;
    }

    /// <summary>The server is being asked; the button waits.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty] public partial string Username { get; set; } = "";
    [ObservableProperty] public partial string Email { get; set; } = "";
    [ObservableProperty] public partial string Password { get; set; } = "";
    [ObservableProperty] public partial string ConfirmPassword { get; set; } = "";
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial string? UsernameError { get; set; }
    [ObservableProperty] public partial string? EmailError { get; set; }
    [ObservableProperty] public partial string? PasswordError { get; set; }
    [ObservableProperty] public partial string? ConfirmPasswordError { get; set; }

    private bool CanRegister() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRegister))]
    private async Task RegisterAsync()
    {
        Error = UsernameError = EmailError = PasswordError = ConfirmPasswordError = null;
        if (Password != ConfirmPassword) { ConfirmPasswordError = "Passwords do not match."; return; }

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
        catch (Exception)
        {
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