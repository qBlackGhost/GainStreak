using System;
using GymApp.Desktop.Models;

namespace GymApp.Desktop.Services;

public class SessionService
{
    public User? CurrentUser { get; private set; }
    public bool IsLoggedIn => CurrentUser is not null;
    public event Action? Changed;

    public void SignIn(User user) { CurrentUser = user; Changed?.Invoke(); }
    public void SignOut() { CurrentUser = null; Changed?.Invoke(); }
}