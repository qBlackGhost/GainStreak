using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace GymApp.Desktop.Views;

public partial class WorkoutsView : UserControl
{
    // "today is Push" in the accent; "today is a rest day" stays grey
    public static readonly IValueConverter NoteBrush = new FuncValueConverter<bool, IBrush>(rest =>
        rest ? new SolidColorBrush(Color.Parse("#B5B5B5")) : new SolidColorBrush(Color.Parse("#C6F432")));
    public static readonly IValueConverter NoteWeight = new FuncValueConverter<bool, FontWeight>(rest =>
        rest ? FontWeight.Normal : FontWeight.SemiBold);

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
