using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using GymApp.App.ViewModels;

namespace GymApp.App.Views;

public partial class ProfileSetupView : UserControl
{
    /// <summary>Below this width height and weight go one under the other.</summary>
    private const double SideBySideMinWidth = 420;

    /// <summary>A divider above every summary row but the first.</summary>
    public static readonly IValueConverter TopLine = new FuncValueConverter<bool, Thickness>(first => new Thickness(0, first ? 0 : 1, 0, 0));

    public ProfileSetupView()
    {
        InitializeComponent();
        BodyRow.SizeChanged += (_, e) =>
        {
            var w = e.NewSize.Width;
            HeightField.Width = WeightField.Width = w >= SideBySideMinWidth ? (w - BodyRow.ItemSpacing) / 2 : w;
        };

        // Enter searches, like the button (only on purpose: Nominatim allows one request a second)
        CityBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || DataContext is not ProfileSetupViewModel vm) return;
            if (vm.SearchCityCommand.CanExecute(null)) vm.SearchCityCommand.Execute(null);
        };
    }
}
