using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GymApp.App.ViewModels;

namespace GymApp.App.Views;

/// <summary>The feedback popup. Escape (handled here, not globally) and a click on the dimmed page close it; opening
/// it puts the cursor in the text box. "Copy instead" (after a failed send) uses the window's clipboard, so it's here.</summary>
public partial class FeedbackView : UserControl
{
    private FeedbackViewModel? _vm;

    public FeedbackView()
    {
        InitializeComponent();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _vm is not null) { _vm.CloseCommand.Execute(null); e.Handled = true; }
        };
        Scrim.PointerPressed += (_, e) => { _vm?.CloseCommand.Execute(null); e.Handled = true; };
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.Opened -= FocusText;
            _vm = DataContext as FeedbackViewModel;
            if (_vm is not null) _vm.Opened += FocusText;
        };
    }

    // After the overlay is made visible, so the box can take focus
    private void FocusText() => Dispatcher.UIThread.Post(
        () => (_vm?.IsIdea == true ? IdeaBox : ProblemBox).Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);

    private async void CopyInstead(object? sender, RoutedEventArgs e)
    {
        if (_vm?.Compose() is not { } text) return;
        var ok = false;
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
                ok = true;
            }
        }
        catch (Exception) { }
        _vm.Copied(ok);
    }
}
