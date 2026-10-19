using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using GymApp.Desktop.ViewModels;

namespace GymApp.Desktop.Views;

/// <summary>The gym picker dialog. Escape closes it (handled here, not globally); Enter searches in the city and
/// address boxes, since Nominatim only allows a search on purpose, never per keystroke.</summary>
public partial class GymPickerView : UserControl
{
    public static readonly IValueConverter IsNotCityMode = new FuncValueConverter<PickerMode, bool>(m => m != PickerMode.City);

    public GymPickerView()
    {
        InitializeComponent();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is GymPickerViewModel vm) { vm.CloseCommand.Execute(null); e.Handled = true; }
        };
        CityBox.KeyDown += (_, e) => Run(e, vm => vm.SearchCityCommand);
        AddressBox.KeyDown += (_, e) => Run(e, vm => vm.SearchAddressCommand);
    }

    private void Run(KeyEventArgs e, System.Func<GymPickerViewModel, ICommand> command)
    {
        if (e.Key != Key.Enter || DataContext is not GymPickerViewModel vm) return;
        var c = command(vm);
        if (c.CanExecute(null)) c.Execute(null);
        e.Handled = true;
    }
}
