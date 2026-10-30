using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using GymApp.App.ViewModels;

namespace GymApp.App.Views;

public partial class ExerciseView : UserControl
{
    /// <summary>Below this content width the PR and history cards go under today's sets.</summary>
    private const double TwoColumnMinWidth = 880;
    /// <summary>How far a finger (or the mouse) must move sideways over "Weekly stats" to turn the page.</summary>
    private const double SwipeDistance = 40;

    private Point? _swipeStart;
    private Point _swipeLast;

    public ExerciseView()
    {
        InitializeComponent();
        Columns.SizeChanged += (_, e) => Columns.Classes.Set("stack", e.NewSize.Width < TwoColumnMinWidth);
        SetupWeekSwipe();
    }

    /// <summary>Swiping "Weekly stats" right shows the weeks before, left the weeks after. The page's vertical scroll
    /// may take the pointer over mid-swipe, so a lost capture ends the swipe too.</summary>
    private void SetupWeekSwipe()
    {
        WeekChart.PointerPressed += (_, e) => _swipeStart = _swipeLast = e.GetPosition(WeekChart);
        WeekChart.PointerMoved += (_, e) => { if (_swipeStart is not null) _swipeLast = e.GetPosition(WeekChart); };
        WeekChart.PointerReleased += (_, e) => EndSwipe(e.GetPosition(WeekChart));
        WeekChart.PointerCaptureLost += (_, _) => EndSwipe(_swipeLast);
    }

    private void EndSwipe(Point end)
    {
        if (_swipeStart is not { } start) return;
        _swipeStart = null;
        if (DataContext is not ExerciseViewModel vm) return;

        var dx = end.X - start.X;
        if (Math.Abs(dx) < SwipeDistance || Math.Abs(dx) < Math.Abs(end.Y - start.Y)) return;
        var command = dx > 0 ? vm.OlderWeeksCommand : vm.NewerWeeksCommand;
        if (command.CanExecute(null)) command.Execute(null);
    }
}
