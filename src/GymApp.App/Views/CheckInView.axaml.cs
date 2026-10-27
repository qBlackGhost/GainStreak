using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace GymApp.App.Views;

public partial class CheckInView : UserControl
{
    public CheckInView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    // Phones: the keyboard's Next key goes from a set's weight to its reps, then to the next set, so a workout can be
    // filled in with one thumb. The desktop keeps Tab for that
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.Source is not TextBox { Classes: var classes } box || !classes.Contains("chipBox")
            || !MainView.IsPhoneLayout(this)) return;
        var boxes = this.GetVisualDescendants().OfType<TextBox>().Where(b => b.Classes.Contains("chipBox") && b.IsEffectivelyVisible).ToList();
        var i = boxes.IndexOf(box);
        if (i >= 0 && i + 1 < boxes.Count) boxes[i + 1].Focus(NavigationMethod.Tab);
        e.Handled = true;
    }
}
