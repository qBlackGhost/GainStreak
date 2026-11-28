using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Transformation;
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
    // 0.28.1: the held row lifts and follows the finger; the rows it passes slide out of its way (a short ease-out),
    // and on release it glides into its slot before the new order is saved and the list redrawn. Everything moves with
    // render transforms on the row containers, so the layout itself never changes during a drag.

    /// <summary>The held row doesn't grow: it fills the list's width, so a bigger row lost its outline's sides to the
    /// list's edges. The lift is the outline, the shadow and the lighter fill. How long the others take to slide and the
    /// row to settle:</summary>
    private const double LiftScale = 1;
    private static readonly TimeSpan SlideTime = TimeSpan.FromMilliseconds(160), SettleTime = TimeSpan.FromMilliseconds(200);

    private Control? _dragRow;        // the held row's container
    private double _pressY;           // where the finger went down, in the list panel's coordinates
    private double _dragDy;           // how far the row has moved from its place

    /// <summary>The row's drag handle under the pointer or focus, if any.</summary>
    private Border? HandleOf(object? source) =>
        (source as Visual)?.GetSelfAndVisualAncestors()
            .TakeWhile(v => v != ChosenList)
            .OfType<Border>()
            .FirstOrDefault(b => b.Name == "Handle");

    private Visual Panel => (Visual?)ChosenList.ItemsPanelRoot ?? ChosenList;

    private static ITransform Shift(double dy, double scale = 1) =>
        TransformOperations.Parse(FormattableString.Invariant($"translateY({dy:0.##}px) scale({scale:0.###})"));

    private static Transitions Slide(TimeSpan time) =>
        [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = time, Easing = new CubicEaseOut() }];

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (HandleOf(e.Source) is not { Tag: DayExerciseItemViewModel item } handle) return;
        if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
        if (ChosenList.ContainerFromIndex(item.Index) is not { } row) return;
        _drag = item;
        _slot = -1;
        _dragRow = row;
        _pressY = e.GetPosition(Panel).Y;
        _dragDy = 0;
        item.IsDragging = true;
        // The held row follows the finger with no delay and draws over the others; they slide when it passes
        row.Transitions = null;
        row.ZIndex = 10;
        row.RenderTransform = Shift(0, LiftScale);
        for (var i = 0; i < ChosenList.ItemCount; i++)
            if (i != item.Index && ChosenList.ContainerFromIndex(i) is { } other) other.Transitions = Slide(SlideTime);
        e.Pointer.Capture(handle);
        handle.Focus();
        // 0.28.1, phones: a finger moving from the handle is the drag's, not the page's. Without this the page's
        // scroll gesture took the touch once the day was long enough to scroll, so the page scrolled instead and the
        // row stayed faded (as on the map, GymsView)
        e.PreventGestureRecognition();
        e.Handled = true;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_drag is null || _dragRow is null || Vm is not { } vm) return;
        e.PreventGestureRecognition();
        AutoScroll(e);

        // Kept within the list: past the first or last place the row would leave the list's own ScrollViewer, which
        // clips it (on phones that viewport is exactly the rows, so the outline's edge was cut off)
        var panelHeight = Panel.Bounds.Height;
        _dragDy = Math.Clamp(e.GetPosition(Panel).Y - _pressY, -_dragRow.Bounds.Top,
            Math.Max(-_dragRow.Bounds.Top, panelHeight - _dragRow.Bounds.Bottom));
        _dragRow.RenderTransform = Shift(_dragDy, LiftScale);

        // The slot is before the first other row whose middle is below the held row's middle (layout positions, not
        // where the rows have slid to)
        var from = _drag.Index;
        var middle = _dragRow.Bounds.Top + _dragDy + _dragRow.Bounds.Height / 2;
        var count = vm.Items.Count;
        var slot = count;
        for (var i = 0; i < count; i++)
        {
            if (i == from || ChosenList.ContainerFromIndex(i) is not { } c) continue;
            if (middle < c.Bounds.Top + c.Bounds.Height / 2) { slot = i; break; }
        }
        if (slot == _slot) return;
        _slot = slot;

        // Rows between the held row's place and the slot slide by its height to open the gap
        var gap = _dragRow.Bounds.Height + RowSpacing;
        for (var i = 0; i < count; i++)
        {
            if (i == from || ChosenList.ContainerFromIndex(i) is not { } c) continue;
            var dy = from < i && i < slot ? -gap : slot <= i && i < from ? gap : 0;
            c.RenderTransform = Shift(dy);
        }
    }

    /// <summary>The list panel's spacing between rows (the StackPanel in DayView.axaml).</summary>
    private double RowSpacing => (ChosenList.ItemsPanelRoot as StackPanel)?.Spacing ?? 8;

    private void AutoScroll(PointerEventArgs e)
    {
        if (ChosenList.FindAncestorOfType<ScrollViewer>() is not { } sv) return;
        var y = e.GetPosition(sv).Y;
        if (y < AutoScrollEdge) sv.Offset = sv.Offset.WithY(System.Math.Max(0, sv.Offset.Y - AutoScrollStep));
        else if (y > sv.Bounds.Height - AutoScrollEdge) sv.Offset = sv.Offset.WithY(sv.Offset.Y + AutoScrollStep);
    }

    private async void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_drag is not { } item || _dragRow is not { } row || Vm is not { } vm) return;
        e.PreventGestureRecognition();
        var slot = _slot;
        // Cleared before letting go of the pointer: losing the capture runs EndDrag, which is for a cancelled drag
        _drag = null;
        _dragRow = null;
        _slot = -1;
        e.Pointer.Capture(null);
        e.Handled = true;

        var from = item.Index;
        var moved = slot >= 0 && slot != from && slot != from + 1;
        var index = !moved ? from : slot > from ? slot - 1 : slot;   // the row leaves its old place first

        // Settle: the row glides to its new place and drops the lift, then the order is saved and the list redrawn
        item.IsDragging = false;
        row.Transitions = Slide(SettleTime);
        row.RenderTransform = Shift(SettleOffset(from, index));
        await System.Threading.Tasks.Task.Delay(SettleTime);

        if (moved)
        {
            await vm.MoveToAsync(item, index);
            FocusHandle(index);
        }
        ResetRows();
    }

    /// <summary>Where the held row ends up, from its own place: the heights of the rows it passed, plus the spacing.</summary>
    private double SettleOffset(int from, int to)
    {
        double offset = 0;
        for (var i = System.Math.Min(from, to); i <= System.Math.Max(from, to); i++)
            if (i != from && ChosenList.ContainerFromIndex(i) is { } c) offset += c.Bounds.Height + RowSpacing;
        return to > from ? offset : -offset;
    }

    /// <summary>Every row back in its layout place, with no transform or transition (after a redraw the containers may
    /// be new ones; this covers both).</summary>
    private void ResetRows()
    {
        for (var i = 0; i < ChosenList.ItemCount; i++)
            if (ChosenList.ContainerFromIndex(i) is { } c)
            {
                c.Transitions = null;
                c.RenderTransform = null;
                c.ZIndex = 0;
            }
    }

    /// <summary>The drag ended without a release (the pointer was taken away): everything slides back.</summary>
    private void EndDrag()
    {
        if (_drag is null) return;
        _drag.IsDragging = false;
        for (var i = 0; i < ChosenList.ItemCount; i++)
            if (ChosenList.ContainerFromIndex(i) is { } c)
            {
                c.Transitions = Slide(SettleTime);
                c.RenderTransform = Shift(0);
            }
        _drag = null;
        _dragRow = null;
        _slot = -1;
        DispatcherTimer.RunOnce(ResetRows, SettleTime);   // once they're back
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
