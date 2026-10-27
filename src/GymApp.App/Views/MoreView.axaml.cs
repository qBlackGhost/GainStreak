using Avalonia;
using Avalonia.Controls;
using GymApp.App.ViewModels;

namespace GymApp.App.Views;

public partial class MoreView : UserControl
{
    public MoreView()
    {
        InitializeComponent();
    }

    // The inbox badge follows the inbox only while the screen shows
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as MoreViewModel)?.Attach();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        (DataContext as MoreViewModel)?.Detach();
        base.OnDetachedFromVisualTree(e);
    }
}
