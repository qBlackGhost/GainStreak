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
            v => FoldPage.MinHeight = _desktopColumns ? 0 : System.Math.Max(0, v.Height - PhonePage.Margin.Top - PhonePage.RowSpacing)));

        // The desktop (frozen app, shown at the presentation): a wide window puts the last 7 days beside today's card
        SizeChanged += (_, e) => ApplyDesktopColumns(e.NewSize.Width >= DesktopTwoColumnMinWidth && GymApplication.Platform is not PhonePlatform);

        // Enter submits, like the buttons
        TodayBox.KeyDown += (_, e) => Run(e, vm => vm.LogCommand);
        GoalBox.KeyDown += (_, e) => Run(e, vm => vm.SetCustomGoalCommand);
    }

    /// <summary>Wide desktop windows: the page is two columns, today's card, Manage food and the goal rows on the left and the
    /// last 7 days, taller, on the right. Anywhere else it is the phones' single column.</summary>
    private const double DesktopTwoColumnMinWidth = 900;
    private bool _desktopColumns;

    private void ApplyDesktopColumns(bool wide)
    {
        if (wide == _desktopColumns) return;
        _desktopColumns = wide;
        if (wide)
        {
            FoldPage.Children.Remove(WeekCard);
            PhonePage.Children.Add(WeekCard);
            Grid.SetColumn(WeekCard, 1);
            Grid.SetRow(WeekCard, 0);
            Grid.SetRowSpan(WeekCard, 2);
            WeekCard.MinHeight = 420;
            PhonePage.ColumnDefinitions = [new ColumnDefinition(1, GridUnitType.Star), new ColumnDefinition(1, GridUnitType.Star)];
            PhonePage.MaxWidth = 1000;
            FoldPage.MinHeight = 0;
        }
        else
        {
            PhonePage.Children.Remove(WeekCard);
            Grid.SetColumn(WeekCard, 0);
            Grid.SetRow(WeekCard, 3);
            Grid.SetRowSpan(WeekCard, 1);
            FoldPage.Children.Add(WeekCard);
            WeekCard.MinHeight = 150;
            PhonePage.ColumnDefinitions = [];
            PhonePage.MaxWidth = 560;
        }
    }

    private void Run(KeyEventArgs e, System.Func<CaloriesViewModel, System.Windows.Input.ICommand> command)
    {
        if (e.Key != Key.Enter || DataContext is not CaloriesViewModel vm) return;
        var c = command(vm);
        if (c.CanExecute(null)) c.Execute(null);
    }
}
