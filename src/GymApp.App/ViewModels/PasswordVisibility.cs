using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GymApp.App.ViewModels;

/// <summary>The phones' Show password button on Log in and Create account, for every password box on the form. It sets
/// the boxes' mask, not TextBox.RevealPassword: a box clears that when it loses focus, so moving to the next field
/// would hide the password again.</summary>
public partial class PasswordVisibility : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Mask), nameof(Label))]
    public partial bool IsShown { get; set; }

    public char Mask => IsShown ? default : '●';
    public string Label => IsShown ? "Hide password" : "Show password";

    [RelayCommand] private void Toggle() => IsShown = !IsShown;
}
