using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using GymApp.App.ViewModels;

namespace GymApp.App.Views;

public partial class CardioCardView : UserControl
{
    private DispatcherTimer? _timer;

    public CardioCardView() => InitializeComponent();

    // The time moves once a second while the card is on screen; the times themselves live in the database
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal,
            (_, _) => (DataContext as CardioCardViewModel)?.Tick());
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer?.Stop();
        _timer = null;
        base.OnDetachedFromVisualTree(e);
    }
}
