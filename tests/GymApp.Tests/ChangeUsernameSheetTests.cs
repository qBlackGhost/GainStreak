using GymApp.App.ViewModels;
using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;
using VM = GymApp.App.ViewModels.ChangeUsernameSheetViewModel;

namespace GymApp.Tests;

/// <summary>The Change username sheet (Phase 28): live feedback, when Change is on, and where refusals show.</summary>
public class ChangeUsernameSheetTests
{
    private sealed class FakeAccounts : IAccountService
    {
        public AccountResult Next = new(true);
        public UsernameChangeCheck Check = new(true, null);
        public DateTime? AllowedAt;
        public List<(string Name, string Password)> Renames { get; } = [];

        public DateTime? UsernameChangeAllowedAt => AllowedAt;
        public Task<UsernameChangeCheck> CheckUsernameChangeAsync() { AllowedAt = Check.AllowedAt; return Task.FromResult(Check); }
        public Task<AccountResult> ChangeUsernameAsync(string newUsername, string currentPassword)
        {
            Renames.Add((newUsername, currentPassword));
            return Task.FromResult(Next);
        }

        public bool? EmailConfirmed => null;
        public Task<AccountResult> SendConfirmCodeAsync() => throw new NotSupportedException();
        public Task<AccountResult> ConfirmEmailAsync(string code) => throw new NotSupportedException();
        public Task<AccountResult> RequestPasswordResetAsync(string email) => throw new NotSupportedException();
        public Task<AccountResult> ResetPasswordAsync(string email, string code, string newPassword) => throw new NotSupportedException();
        public Task<AccountResult> RegisterAsync(string username, string email, string password) => throw new NotSupportedException();
        public Task<AccountResult> SignInAsync(string login, string password) => throw new NotSupportedException();
        public Task<AccountResult> MoveOnlineAsync(int localUserId, string password, string? email = null, string? username = null) => throw new NotSupportedException();
        public Task<bool> RestoreAsync() => throw new NotSupportedException();
        public Task LogOutAsync() => throw new NotSupportedException();
        public Task<AccountResult> ChangeEmailAsync(string newEmail, string currentPassword) => throw new NotSupportedException();
        public Task<AccountResult> ChangePasswordAsync(string currentPassword, string newPassword) => throw new NotSupportedException();
        public Task<AccountResult> DeleteAccountAsync(string password) => throw new NotSupportedException();
    }

    private readonly FakeAccounts _accounts = new();
    private int _closed;
    private string? _renamedTo;

    private VM Sheet(string current = "cristi") =>
        new(_accounts, current, () => _closed++, name => _renamedTo = name);

    private static readonly DateTime InAMonth = DateTime.UtcNow.AddDays(30);

    [Fact] public void LiveFeedback_FollowsTheRegistrationRules()
    {
        var vm = Sheet();
        Assert.Equal((VM.RulesHint, false, false, "0 / 32"), (vm.Hint, vm.IsHintOk, vm.IsHintError, vm.CounterText));

        vm.NewUsername = "ab";
        Assert.Equal(("Username must be at least 3 characters.", true), (vm.Hint, vm.IsHintError));
        vm.NewUsername = "gym rat";
        Assert.True(vm.IsHintError);
        vm.NewUsername = "2026";
        Assert.Equal(Credentials.NeedsALetterMessage, vm.Hint);
        vm.NewUsername = "admin";
        Assert.True(vm.IsHintError);
        vm.NewUsername = "cristi";
        Assert.Equal((VM.SameNameHint, false, false), (vm.Hint, vm.IsHintOk, vm.IsHintError));

        vm.NewUsername = " cristi_lifts ";
        Assert.Equal((VM.RulesHint, true, "12 / 32"), (vm.Hint, vm.IsHintOk, vm.CounterText));
    }

    [Fact] public void Change_IsOn_OnlyForAValidNameAndAPassword()
    {
        var vm = Sheet();
        vm.NewUsername = "cristi_lifts";
        Assert.False(vm.ChangeCommand.CanExecute(null));
        vm.Password = "pw";
        Assert.True(vm.ChangeCommand.CanExecute(null));
        vm.NewUsername = "ab";
        Assert.False(vm.ChangeCommand.CanExecute(null));
    }

    [Fact] public async Task Success_ClosesAndPassesTheNewName()
    {
        var vm = Sheet();
        _accounts.Next = AccountResult.Ok(new User { Username = "cristi_lifts" });
        vm.NewUsername = "cristi_lifts";
        vm.Password = "pw";

        await vm.ChangeCommand.ExecuteAsync(null);

        Assert.Equal(("cristi_lifts", "pw"), Assert.Single(_accounts.Renames));
        Assert.Equal((1, "cristi_lifts"), (_closed, _renamedTo));
    }

    [Fact] public async Task Refusals_ShowUnderTheRightBox_AndTypingClearsThem()
    {
        var vm = Sheet();
        vm.NewUsername = "gymrat";
        vm.Password = "pw";

        _accounts.Next = AccountResult.Fail("That username is taken.", FormField.Username);
        await vm.ChangeCommand.ExecuteAsync(null);
        Assert.Equal(("That username is taken.", false), (vm.UsernameError, vm.ShowHint));

        _accounts.Next = AccountResult.Fail("That password is not right.", FormField.Password);
        vm.NewUsername = "gymrat2";
        Assert.Null(vm.UsernameError);
        await vm.ChangeCommand.ExecuteAsync(null);
        Assert.Equal("That password is not right.", vm.PasswordError);
        vm.Password = "pw2";
        Assert.Null(vm.PasswordError);
        Assert.Equal(0, _closed);
    }

    [Fact] public async Task Offline_WhenOpened_KeepsChangeOff()
    {
        _accounts.Check = new UsernameChangeCheck(false, null);
        var vm = Sheet();
        await vm.LoadAsync();
        vm.NewUsername = "cristi_lifts";
        vm.Password = "pw";

        Assert.True(vm.IsOffline);
        Assert.False(vm.ChangeCommand.CanExecute(null));
    }

    [Fact] public async Task Offline_WhenSaving_ShowsTheOfflineNote()
    {
        var vm = Sheet();
        vm.NewUsername = "cristi_lifts";
        vm.Password = "pw";
        _accounts.Next = AccountResult.Fail(AccountService.RenameNeedsConnectionMessage);

        await vm.ChangeCommand.ExecuteAsync(null);

        Assert.True(vm.IsOffline);
        Assert.False(vm.ChangeCommand.CanExecute(null));
    }

    [Fact] public async Task Waiting_ShowsTheDate_ButCapitalsOnlyStillWork()
    {
        _accounts.Check = new UsernameChangeCheck(true, InAMonth);
        var vm = Sheet();
        await vm.LoadAsync();
        vm.Password = "pw";

        Assert.Equal(Credentials.UsernameChangeTooSoonMessage(InAMonth), vm.WaitNotice);
        vm.NewUsername = "cristi_lifts";
        Assert.Equal((vm.WaitNotice, false), (vm.Hint, vm.ChangeCommand.CanExecute(null)));

        vm.NewUsername = "Cristi";
        Assert.Equal((VM.CapitalsOnlyHint, true), (vm.Hint, vm.ChangeCommand.CanExecute(null)));
    }
}
