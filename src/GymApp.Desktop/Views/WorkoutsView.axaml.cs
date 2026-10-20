using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;

namespace GymApp.Desktop.Views;

public partial class WorkoutsView : UserControl
{
    // "Today is a rest day. Next up: Upper. You can still…", around the bold day name
    public static readonly IValueConverter NextUp = new FuncValueConverter<string?, string>(next =>
        next is null ? " You can still open any day to plan or log sets." : " Next up: ");
    public static readonly IValueConverter NextUpEnd = new FuncValueConverter<string?, string>(next =>
        next is null ? "" : ". You can still open any day to plan or log sets.");

    public WorkoutsView()
    {
        InitializeComponent();
    }
}
