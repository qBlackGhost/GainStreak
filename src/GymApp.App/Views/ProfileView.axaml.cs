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

    public ProfileView()
    {
        InitializeComponent();
        // The phones' pages (0.23.1): the view model shows them only when laid out for a phone
        SizeChanged += (_, _) => SyncPhone();
        AttachedToVisualTree += (_, _) => SyncPhone();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as ProfileViewModel;
            if (_vm is not null) _vm.PropertyChanged += OnVmChanged;
            SyncPhone();
        };

        Columns.SizeChanged += (_, e) =>
        {
            Columns.Classes.Set("stack", e.NewSize.Width < TwoColumnMinWidth);
            Header.Classes.Set("stack", e.NewSize.Width < TwoColumnMinWidth);
        };

        // Enter submits, like the buttons (city search runs only on purpose: Nominatim allows one request a second)
        CityBox.KeyDown += (_, e) => Run(e, vm => vm.SearchCityCommand);
        WeightBox.KeyDown += (_, e) => Run(e, vm => vm.LogWeightCommand);
    }

    private ProfileViewModel? _vm;

    private void SyncPhone()
    {
        if (_vm is not null) _vm.IsPhone = MainView.IsPhoneLayout(this);
    }

    // A new page starts at its top
    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProfileViewModel.Page)) Scroller.Offset = default;
    }

    private void Run(KeyEventArgs e, System.Func<ProfileViewModel, ICommand> command)
    {
        if (e.Key != Key.Enter || DataContext is not ProfileViewModel vm) return;
        var c = command(vm);
        if (c.CanExecute(null)) c.Execute(null);
    }
}
