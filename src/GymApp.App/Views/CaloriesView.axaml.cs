using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using GymApp.App.ViewModels;

namespace GymApp.App.Views;

public partial class CaloriesView : UserControl
{
    /// <summary>Below this content width the goal column goes under today's card.</summary>
    private const double TwoColumnMinWidth = 880;

    /// <summary>Lifts the target band to its lower edge (pixels from the chart's bottom).</summary>
    public static readonly IValueConverter BottomMargin =
        new FuncValueConverter<double, Thickness>(bottom => new Thickness(0, 0, 0, bottom));

    public CaloriesView()
    {
        InitializeComponent();
        Columns.SizeChanged += (_, e) => Columns.Classes.Set("stack", e.NewSize.Width < TwoColumnMinWidth);

        // Phones: the Calories card, Manage food and the chart fill the screen, so the chart's card (the star row) takes what is
        // left and the goal rows sit just below the fold; on a short phone the chart stops at its minimum and the page scrolls
        Scroller.GetObservable(ScrollViewer.ViewportProperty).Subscribe(new Avalonia.Reactive.AnonymousObserver<Size>(
            v => FoldPage.MinHeight = System.Math.Max(0, v.Height - PhonePage.Margin.Top - PhonePage.Spacing)));

        // Enter submits, like the buttons
        TodayBox.KeyDown += (_, e) => Run(e, vm => vm.LogCommand);
        GoalBox.KeyDown += (_, e) => Run(e, vm => vm.SetCustomGoalCommand);
    }

    private void Run(KeyEventArgs e, System.Func<CaloriesViewModel, System.Windows.Input.ICommand> command)
    {
        if (e.Key != Key.Enter || DataContext is not CaloriesViewModel vm) return;
        var c = command(vm);
        if (c.CanExecute(null)) c.Execute(null);
    }
}
