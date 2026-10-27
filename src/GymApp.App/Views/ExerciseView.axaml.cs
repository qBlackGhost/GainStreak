using Avalonia.Controls;

namespace GymApp.App.Views;

public partial class ExerciseView : UserControl
{
    /// <summary>Below this content width the PR and history cards go under today's sets.</summary>
    private const double TwoColumnMinWidth = 880;

    public ExerciseView()
    {
        InitializeComponent();
        Columns.SizeChanged += (_, e) => Columns.Classes.Set("stack", e.NewSize.Width < TwoColumnMinWidth);
    }
}
