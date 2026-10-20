using System;
using GymApp.Data.Models;

namespace GymApp.Services;

public class SessionService
{
    public User? CurrentUser { get; private set; }
    public bool IsLoggedIn => CurrentUser is not null;

    /// <summary>Signed in from the cached password because the server couldn't be reached; friends show cached data.</summary>
    public bool SignedInOffline { get; private set; }

    /// <summary>Why the last sign-out happened when the user didn't ask for it (e.g. "Your session ended."), for the
    /// login screen. Cleared by the next sign-in.</summary>
    public string? SignOutNotice { get; private set; }

    public event Action? Changed;

    public void SignIn(User user, bool offline = false)
    {
        CurrentUser = user;
        SignedInOffline = offline;
        SignOutNotice = null;
        Changed?.Invoke();
    }

    public void SignOut(string? notice = null)
    {
        CurrentUser = null;
        SignedInOffline = false;
        SignOutNotice = notice;
        Changed?.Invoke();
    }
}
