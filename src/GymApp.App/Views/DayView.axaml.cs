using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GymApp.App.ViewModels;

namespace GymApp.App.Views;

public partial class DayView : UserControl
{
    /// <summary>Below this content width the picker goes under the day's list and the page scrolls as a whole.</summary>
    private const double TwoColumnMinWidth = 900;

    /// <summary>How close to the list's top or bottom edge a drag scrolls it, and by how much per move.</summary>
    private const double AutoScrollEdge = 32, AutoScrollStep = 12;

    private DayExerciseItemViewModel? _drag;
    private int _slot = -1;   // where the dragged row would go: 0..count, between rows

    public DayView()
    {
        InitializeComponent();
        Columns.SizeChanged += (_, e) => LayOut(e.NewSize.Width);
        SizeChanged += (_, e) => AddSheetBody.Height = System.Math.Floor(e.NewSize.Height * 0.86);

        ChosenList.AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        ChosenList.AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel);
        ChosenList.AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel);
        ChosenList.AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag(), RoutingStrategies.Bubble);
        ChosenList.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private DayViewModel? Vm => DataContext as DayViewModel;

    /// <summary>Wide: the page fits the window, so each list scrolls inside its card when it's too long.
    /// Narrow: the cards stack and the page scrolls; the lists are capped (see the styles).</summary>
    private void LayOut(double width)
    {
        PlacePicker(MainView.IsPhoneLayout(this));
        var stack = width < TwoColumnMinWidth;
        Columns.Classes.Set("stack", stack);
        Columns.RowDefinitions[0].Height = stack ? GridLength.Auto : new GridLength(1, GridUnitType.Star);   // stacked: no stretching
        PageScroll.VerticalScrollBarVisibility = stack ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                                                       : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
    }

    /// <summary>Phones (the GainStreak Mobile canvas): the picker card lives in the bottom sheet, opened by "Add
    /// exercises". Wider: back beside (or under) the day's list.</summary>
    private void PlacePicker(bool phone)
    {
        if (phone && AddCard.Parent == Columns)
        {
            Columns.Children.Remove(AddCard);
            AddSheetBody.Child = AddCard;
        }
        else if (!phone && AddCard.Parent == AddSheetBody)
        {
            AddSheetBody.Child = null;
            Columns.Children.Add(AddCard);
            if (Vm is { } vm) vm.IsAddOpen = false;
        }
    }

    private void OnAddScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is { } vm) vm.IsAddOpen = false;
        e.Handled = true;
    }

    // ===== Drag to reorder (by the handle) =====

    /// <summary>The row's drag handle under the pointer or focus, if any.</summary>
    private Border? HandleOf(object? source) =>
        (source as Visual)?.GetSelfAndVisualAncestors()
            .TakeWhile(v => v != ChosenList)
            .OfType<Border>()
            .FirstOrDefault(b => b.Name == "Handle");

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (HandleOf(e.Source) is not { Tag: DayExerciseItemViewModel item } handle) return;
        if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
        _drag = item;
        _slot = -1;
        item.IsDragging = true;
        e.Pointer.Capture(handle);
        handle.Focus();
        e.Handled = true;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_drag is null || Vm is not { } vm) return;
        AutoScroll(e);

        // The slot is before the first row whose middle is below the pointer
        var y = e.GetPosition(ChosenList).Y;
        var count = vm.Items.Count;
        var slot = count;
        for (var i = 0; i < count; i++)
        {
            if (ChosenList.ContainerFromIndex(i) is not { } c) continue;
            var top = c.TranslatePoint(new Point(0, 0), ChosenList)?.Y ?? 0;
            if (y < top + c.Bounds.Height / 2) { slot = i; break; }
        }
        if (slot == _slot) return;
        _slot = slot;

        foreach (var row in vm.Items) row.IsDropAbove = row.IsDropBelow = false;
        if (slot == _drag.Index || slot == _drag.Index + 1) return;   // it would stay where it is
        if (slot < count) vm.Items[slot].IsDropAbove = true;
        else vm.Items[count - 1].IsDropBelow = true;
    }

    private void AutoScroll(PointerEventArgs e)
    {
        if (ChosenList.FindAncestorOfType<ScrollViewer>() is not { } sv) return;
        var y = e.GetPosition(sv).Y;
        if (y < AutoScrollEdge) sv.Offset = sv.Offset.WithY(System.Math.Max(0, sv.Offset.Y - AutoScrollStep));
        else if (y > sv.Bounds.Height - AutoScrollEdge) sv.Offset = sv.Offset.WithY(sv.Offset.Y + AutoScrollStep);
    }

    private async void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_drag is not { } item || Vm is not { } vm) return;
        var slot = _slot;
        e.Pointer.Capture(null);
        EndDrag();
        e.Handled = true;

        if (slot < 0 || slot == item.Index || slot == item.Index + 1) return;
        var index = slot > item.Index ? slot - 1 : slot;   // the row leaves its old place first
        await vm.MoveToAsync(item, index);
        FocusHandle(index);
    }

    private void EndDrag()
    {
        if (_drag is null) return;
        _drag.IsDragging = false;
        if (Vm is { } vm)
            foreach (var row in vm.Items) row.IsDropAbove = row.IsDropBelow = false;
        _drag = null;
        _slot = -1;
    }

    // Keyboard: Up / Down on a focused handle moves that row
    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down) || Vm is not { } vm) return;
        if (HandleOf(e.Source) is not { Tag: DayExerciseItemViewModel item }) return;
        var direction = e.Key == Key.Up ? -1 : +1;
        var index = item.Index + direction;
        if (index < 0 || index >= vm.Items.Count) return;
        e.Handled = true;
        await vm.MoveAsync(item, direction);
        FocusHandle(index);
    }

    /// <summary>After a move the rows are rebuilt; keep the focus on the moved row's handle.</summary>
    private void FocusHandle(int index) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (ChosenList.ContainerFromIndex(index) is { } c &&
                c.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "Handle") is { } h)
            {
                h.Focus();
                h.BringIntoView();
            }
        }, DispatcherPriority.Loaded);
}
