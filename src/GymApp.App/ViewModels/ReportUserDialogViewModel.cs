using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Contracts;
using GymApp.Services;
using GymApp.Social;

namespace GymApp.App.ViewModels;

public partial class ReportReasonViewModel(UserReportReason reason, ReportUserDialogViewModel owner) : ObservableObject
{
    public UserReportReason Reason => reason;
    public string Label => UserReportRules.Label(reason);

    [ObservableProperty] public partial bool IsSelected { get; set; }

    [RelayCommand] private void Choose() => owner.Choose(this);
}

/// <summary>The popup over Friends and the inbox: report a user, block them, or both. Block asks for confirmation first:
/// the button turns into "Confirm block". The screen that opened it reloads and shows the notice it gets back.</summary>
public partial class ReportUserDialogViewModel(IFriendService friends, IUserReportService reports, SessionService session) : ObservableObject
{
    private static readonly UserReportReason[] AllReasons =
    [
        UserReportReason.Username, UserReportReason.Harassment, UserReportReason.Spam,
        UserReportReason.SharedContent, UserReportReason.Other
    ];

    private int _otherId;
    private Func<string, Task>? _done;

    public ObservableCollection<ReportReasonViewModel> Reasons { get; } = new();

    [ObservableProperty] public partial bool IsOpen { get; set; }
    [ObservableProperty] public partial string Username { get; set; } = "";
    [ObservableProperty] public partial string Note { get; set; } = "";
    [ObservableProperty] public partial bool AlsoBlock { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BlockLabel))]
    public partial bool IsConfirmingBlock { get; set; }

    public string Title => $"Report or block {Username}";
    public string AlsoBlockLabel => $"Also block {Username}";
    public string BlockLabel => IsConfirmingBlock ? "Confirm block" : "Block";
    public int MaxNoteLength => UserReportRules.MaxNoteLength;

    partial void OnUsernameChanged(string value)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(AlsoBlockLabel));
    }

    /// <param name="otherId">The service's own id for the user (a server id in remote mode).</param>
    /// <param name="done">Called with the notice to show, after the popup closed.</param>
    public void Open(int otherId, string username, Func<string, Task> done)
    {
        _otherId = otherId;
        _done = done;
        Username = username;
        Note = "";
        AlsoBlock = true;
        Error = null;
        IsConfirmingBlock = false;
        Reasons.Clear();
        foreach (var r in AllReasons) Reasons.Add(new ReportReasonViewModel(r, this));
        IsOpen = true;
    }

    internal void Choose(ReportReasonViewModel reason)
    {
        foreach (var r in Reasons) r.IsSelected = r == reason;
        Error = null;
    }

    [RelayCommand] private void Close() => IsOpen = false;

    [RelayCommand]
    private async Task SendAsync()
    {
        if (Reasons.FirstOrDefault(r => r.IsSelected) is not { } reason) { Error = "Pick a reason first."; return; }
        var name = Username;
        try
        {
            if (await reports.ReportAsync(_otherId, reason.Reason, Note) is { } error) { Error = error; return; }
            await FinishAsync(!AlsoBlock ? "Report sent. Thanks for letting us know."
                : await BlockAsync() is { } blockError ? $"Report sent, but {name} couldn't be blocked: {blockError}"
                : $"Report sent, and {name} is blocked.");
        }
        catch (Exception)
        {
            Error = "Something went wrong. Try again.";
        }
    }

    [RelayCommand]
    private async Task BlockOnlyAsync()
    {
        if (!IsConfirmingBlock) { IsConfirmingBlock = true; return; }
        var name = Username;
        try
        {
            if (await BlockAsync() is { } error) { Error = error; return; }
            await FinishAsync($"{name} is blocked.");
        }
        catch (Exception)
        {
            Error = "Something went wrong. Try again.";
        }
    }

    /// <summary>Null on success, else the message.</summary>
    private async Task<string?> BlockAsync()
    {
        if (session.CurrentUser?.Id is not { } me) return "You're signed out.";
        var r = await friends.BlockAsync(me, _otherId);
        return r.Success ? null : r.Error;
    }

    private async Task FinishAsync(string notice)
    {
        IsOpen = false;
        if (_done is { } done) await done(notice);
    }
}
