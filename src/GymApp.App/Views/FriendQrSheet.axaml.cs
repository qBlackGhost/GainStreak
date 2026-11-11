using System;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using GymApp.App.ViewModels;

namespace GymApp.App.Views;

/// <summary>The QR sheet. Escape and a click on the dimmed page close it. Copy link uses the window's clipboard, so
/// it's here; the view model shows "Link copied".</summary>
public partial class FriendQrSheet : UserControl
{
    /// <summary>Copy link takes the whole row when there's no Share button (the desktop).</summary>
    public static readonly IValueConverter SpanWhen =
        new FuncValueConverter<bool, int>(canShare => canShare ? 1 : 2);

    public FriendQrSheet()
    {
        InitializeComponent();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is FriendQrViewModel vm) { vm.CloseCommand.Execute(null); e.Handled = true; }
        };
        Scrim.PointerPressed += (_, e) =>
        {
            if (e.Source == Scrim && DataContext is FriendQrViewModel vm) { vm.CloseCommand.Execute(null); e.Handled = true; }
        };
    }

    private async void CopyLink(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FriendQrViewModel { Link: { } link } vm) return;
        var ok = false;
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(link);
                ok = true;
            }
        }
        catch (Exception) { }
        vm.Copied(ok);
    }
}
