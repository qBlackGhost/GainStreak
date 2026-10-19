using Avalonia.Controls;

namespace GymApp.Desktop.Views;

public partial class SplitDetailView : UserControl
{
    /// <summary>Below this content width "About this split" goes under the setup card.</summary>
    private const double TwoColumnMinWidth = 880;

    public SplitDetailView()
    {
        InitializeComponent();
        Columns.SizeChanged += (_, e) => Columns.Classes.Set("stack", e.NewSize.Width < TwoColumnMinWidth);
    }
}
