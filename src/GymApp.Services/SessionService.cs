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

    /// <summary>Signed in straight after creating the account, so the app opens the profile setup instead of Home.</summary>
    public bool IsNewAccount { get; private set; }

    public event Action? Changed;

    /// <summary>The signed-in user's own details changed in place (a new username, Phase 28) without anyone signing in
    /// or out. Unlike <see cref="Changed"/>, nothing navigates: screens showing the name just read it again.</summary>
    public event Action? UserUpdated;

    /// <summary>Raises <see cref="UserUpdated"/>, after a change to <see cref="CurrentUser"/>'s fields.</summary>
    public void NotifyUserUpdated()
    {
        if (CurrentUser is not null) UserUpdated?.Invoke();
    }

    public void SignIn(User user, bool offline = false, bool newAccount = false)
    {
        CurrentUser = user;
        SignedInOffline = offline;
        IsNewAccount = newAccount;
        SignOutNotice = null;
        Changed?.Invoke();
    }

    public void SignOut(string? notice = null)
    {
        CurrentUser = null;
        SignedInOffline = false;
        IsNewAccount = false;
        SignOutNotice = notice;
        Changed?.Invoke();
    }
}
