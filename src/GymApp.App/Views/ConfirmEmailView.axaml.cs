using Avalonia.Controls;

namespace GymApp.App.Views;

public partial class ConfirmEmailView : UserControl
{
    public ConfirmEmailView()
    {
        InitializeComponent();
        FormKeys.Attach(this);
    }
}
