using GymApp.App.ViewModels;
using GymApp.Services;

namespace GymApp.Tests;

public class RegisterTermsTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gymapp-terms-" + Guid.NewGuid().ToString("N"));
    private readonly SessionService _session = new();
    private readonly RegisterViewModel _vm;

    public RegisterTermsTests()
    {
        var accounts = new LocalAccountService(new AuthService(_db), new RememberMeService(_db, _session), _session, _db);
        _vm = new RegisterViewModel(accounts, () => null!, new ErrorLog(_dir))
        {
            Username = "newlifter", Email = "new@example.com", Password = "correct horse 1", ConfirmPassword = "correct horse 1"
        };
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact] public async Task NoAccount_UntilTheTermsAreAgreedTo()
    {
        await _vm.RegisterCommand.ExecuteAsync(null);

        Assert.NotNull(_vm.TermsError);
        Assert.Null(_session.CurrentUser);
        using var db = _db.CreateDbContext();
        Assert.Empty(db.Users);
    }

    [Fact] public async Task Ticking_ClearsTheError_AndCreatesTheAccount()
    {
        await _vm.RegisterCommand.ExecuteAsync(null);

        _vm.AgreedToTerms = true;
        Assert.Null(_vm.TermsError);

        await _vm.RegisterCommand.ExecuteAsync(null);
        Assert.Equal("newlifter", _session.CurrentUser?.Username);
    }
}
