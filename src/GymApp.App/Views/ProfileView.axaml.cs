using System.Globalization;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using GymApp.App.ViewModels;

namespace GymApp.App.Views;

public partial class ProfileView : UserControl
{
    /// <summary>Below this content width the right column (home city, account, appearance) goes under the left.</summary>
    private const double TwoColumnMinWidth = 880;

    /// <summary>True when the bound index equals the parameter: marks a segmented option as the selected one.</summary>
    public static readonly IValueConverter IsIndex = new FuncValueConverter<int, object?, bool>((index, parameter) =>
        int.TryParse(parameter?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) && p == index);

    /// <summary>Centers the trend's 10 px end dot on the last point.</summary>
    public static readonly IValueConverter Minus5 = new FuncValueConverter<double, double>(v => v - 5);

    public ProfileView()
    {
        InitializeComponent();
        Columns.SizeChanged += (_, e) =>
        {
            Columns.Classes.Set("stack", e.NewSize.Width < TwoColumnMinWidth);
            Header.Classes.Set("stack", e.NewSize.Width < TwoColumnMinWidth);
        };

        // Enter submits, like the buttons (city search runs only on purpose: Nominatim allows one request a second)
        CityBox.KeyDown += (_, e) => Run(e, vm => vm.SearchCityCommand);
        WeightBox.KeyDown += (_, e) => Run(e, vm => vm.LogWeightCommand);
    }

    private void Run(KeyEventArgs e, System.Func<ProfileViewModel, ICommand> command)
    {
        if (e.Key != Key.Enter || DataContext is not ProfileViewModel vm) return;
        var c = command(vm);
        if (c.CanExecute(null)) c.Execute(null);
    }
}
