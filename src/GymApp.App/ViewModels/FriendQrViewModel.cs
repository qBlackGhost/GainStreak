using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Services;
using GymApp.Social;

namespace GymApp.App.ViewModels;

/// <summary>
/// "Your QR code" (Phase 21, the GainStreak Mobile canvas's Friends · Your QR code): the invite link as a QR code,
/// Copy link, Share (phones) and Reset link with its confirm. Opened from Friends' "Show my QR code". The view does the
/// copying (the clipboard belongs to the window) and reports back through <see cref="Copied"/>.
/// </summary>
public partial class FriendQrViewModel(IFriendService friends, SessionService session, IPlatform platform) : ObservableObject
{
    public const string OfflineSavedText = "Offline: this is your saved link.";
    public const string JustResetText = "New link ready. The old one no longer works.";

    /// <summary>How long "Link copied" shows.</summary>
    public static TimeSpan CopiedFor { get; set; } = TimeSpan.FromSeconds(2);

    [ObservableProperty] public partial bool IsOpen { get; set; }
    [ObservableProperty] public partial bool IsLoading { get; private set; }

    /// <summary>The full link: what the QR code holds, Copy copies and Share sends.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLink), nameof(HasNoLink), nameof(LinkText), nameof(CanShare), nameof(CanReset))]
    [NotifyCanExecuteChangedFor(nameof(ShareCommand), nameof(AskResetCommand))]
    public partial string? Link { get; private set; }

    public bool HasLink => Link is not null;
    public bool HasNoLink => Link is null && !IsLoading;

    /// <summary>The link without "https://", under the code.</summary>
    public string LinkText => Link is null ? "" : Link.Replace("https://", "").Replace("http://", "");

    public string Username => session.CurrentUser is { } u ? "@" + u.Username : "";

    /// <summary>The saved link, the server couldn't be reached.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReset))]
    public partial bool IsOfflineSaved { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotCopied))]
    public partial bool IsCopied { get; private set; }

    public bool IsNotCopied => !IsCopied;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReset))]
    public partial bool IsConfirmingReset { get; private set; }

    [ObservableProperty] public partial string? Notice { get; private set; }
    [ObservableProperty] public partial string? Error { get; private set; }

    /// <summary>"You're offline", with no saved link to show instead.</summary>
    [ObservableProperty] public partial string? NoLinkText { get; private set; }

    public bool CanShare => platform.CanShare && HasLink;

    /// <summary>Reset needs the server: hidden offline and while its confirm shows.</summary>
    public bool CanReset => HasLink && !IsOfflineSaved && !IsConfirmingReset;

    private int _copyGeneration;

    public async Task OpenAsync()
    {
        IsOpen = true;
        IsCopied = IsConfirmingReset = false;
        Notice = Error = null;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (session.CurrentUser is not { } me) return;
        IsLoading = true;
        OnPropertyChanged(nameof(HasNoLink));
        try
        {
            var r = await friends.GetInviteCodeAsync(me.Id);
            Show(r);
            NoLinkText = r.Success ? null : r.Error == GymApp.Client.ApiResult.OfflineMessage
                ? "Connect to the internet to get your link." : r.Error;
        }
        catch (Exception)
        {
            Link = null;
            NoLinkText = "Could not get your link. Try again.";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasNoLink));
        }
    }

    private void Show(InviteCodeResult r)
    {
        Link = r.Success ? InviteLink.For(r) : null;
        IsOfflineSaved = r.Success && r.IsStale;
        Notice = IsOfflineSaved ? OfflineSavedText : null;
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        IsConfirmingReset = false;
    }

    /// <summary>The view calls this with whether the clipboard took the link: "Link copied" for <see cref="CopiedFor"/>.</summary>
    public async void Copied(bool ok)
    {
        Error = ok ? null : "Could not copy the link.";
        if (!ok) return;
        IsCopied = true;
        var generation = ++_copyGeneration;
        await Task.Delay(CopiedFor);
        if (generation == _copyGeneration) IsCopied = false;
    }

    [RelayCommand(CanExecute = nameof(CanShare))]
    private void Share()
    {
        if (Link is { } link) platform.Share(link);
    }

    [RelayCommand(CanExecute = nameof(HasLink))]
    private void AskReset() => IsConfirmingReset = true;

    [RelayCommand]
    private void CancelReset() => IsConfirmingReset = false;

    [RelayCommand]
    private async Task ConfirmResetAsync()
    {
        if (session.CurrentUser is not { } me) return;
        IsConfirmingReset = false;
        try
        {
            var r = await friends.ResetInviteCodeAsync(me.Id);
            if (!r.Success) { Error = r.Error; return; }
            Show(r);
            IsCopied = false;
            Error = null;
            Notice = JustResetText;
        }
        catch (Exception)
        {
            Error = "Could not reset your link. Try again.";
        }
    }
}
