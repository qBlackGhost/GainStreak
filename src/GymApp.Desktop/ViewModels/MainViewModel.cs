using System;
using CommunityToolkit.Mvvm.ComponentModel;
using GymApp.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GymApp.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly SessionService _session;
    private readonly IServiceProvider _sp;

    [ObservableProperty]
    public partial ViewModelBase Current { get; set; } = null!;

    public MainViewModel(SessionService session, IServiceProvider sp)
    {
        _session = session;
        _sp = sp;
        _session.Changed += Refresh;
        Refresh();
    }

    private void Refresh() =>
        Current = _session.IsLoggedIn
            ? _sp.GetRequiredService<HomeViewModel>()
            : _sp.GetRequiredService<LoginViewModel>();

    public void ShowRegister() => Current = _sp.GetRequiredService<RegisterViewModel>();
    public void ShowLogin() => Current = _sp.GetRequiredService<LoginViewModel>();
}