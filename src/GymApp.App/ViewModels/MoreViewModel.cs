using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Services;

namespace GymApp.App.ViewModels;

/// <summary>The phones' More tab (16d): the profile card as in the sidebar, then the screens that have no tab of their
/// own (Splits, Map, Inbox) and Feedback, and the version.</summary>
public partial class MoreViewModel : ViewModelBase
{
    private readonly Func<MainViewModel> _shell;
    private readonly IAccountService _accounts;
    private readonly InboxService _inbox;

    public string Name { get; }
    public string Initials { get; }
    public string VersionText { get; } = $"GainStreak {AppVersion.Current}";

    /// <summary>New in the inbox, shown as a badge on its row; 0 hides it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnread))]
    public partial int Unread { get; private set; }

    public bool HasUnread => Unread > 0;

    public MoreViewModel(SessionService session, IAccountService accounts, InboxService inbox, Func<MainViewModel> shell)
    {
        _accounts = accounts;
        _inbox = inbox;
        _shell = shell;
        Name = session.CurrentUser?.Username ?? "";
        Initials = Name.Length == 0 ? "?" : Name[..Math.Min(2, Name.Length)].ToUpperInvariant();
        Unread = inbox.UnreadCount;
    }

    /// <summary>Follows the inbox while the screen shows (MoreView attaches and detaches it).</summary>
    public void Attach() { _inbox.UnreadChanged += OnUnread; OnUnread(); }
    public void Detach() => _inbox.UnreadChanged -= OnUnread;
    private void OnUnread() => Unread = _inbox.UnreadCount;

    [RelayCommand] private void OpenProfile() => _shell().ShowProfile();
    [RelayCommand] private void OpenSplits() => _shell().ShowSplits();
    [RelayCommand] private void OpenMap() => _shell().ShowGyms();
    [RelayCommand] private void OpenInbox() => _shell().ShowInbox();
    [RelayCommand] private void OpenFeedback() => _shell().ShowFeedback();
    [RelayCommand] private Task LogOut() => _accounts.LogOutAsync();
}
