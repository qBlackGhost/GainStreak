using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Threading;
using GymApp.App.ViewModels;

namespace GymApp.App.Views;

/// <summary>The points history popup. Escape (handled here, not globally) and a click on the dimmed page close it;
/// opening it moves focus to the close button so Escape works right away.</summary>
public partial class PointsHistoryView : UserControl
{
    /// <summary>Week headings are set in capitals, like the design ("THIS WEEK").</summary>
    public static readonly IValueConverter Upper =
        new FuncValueConverter<string?, string>(s => (s ?? "").ToUpper(CultureInfo.InvariantCulture));

    private PointsHistoryViewModel? _vm;

    public PointsHistoryView()
    {
        InitializeComponent();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _vm is not null) { _vm.CloseCommand.Execute(null); e.Handled = true; }
        };
        Scrim.PointerPressed += (_, e) => { _vm?.CloseCommand.Execute(null); e.Handled = true; };
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.Opened -= FocusClose;
            _vm = DataContext as PointsHistoryViewModel;
            if (_vm is not null) _vm.Opened += FocusClose;
        };
    }

    // After the overlay is made visible, so the button can take focus
    private void FocusClose() => Dispatcher.UIThread.Post(() => CloseButton.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
}
