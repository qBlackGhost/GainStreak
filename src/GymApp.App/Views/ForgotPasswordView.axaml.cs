using Avalonia.Controls;

namespace GymApp.App.Views;

public partial class ForgotPasswordView : UserControl
{
    public ForgotPasswordView()
    {
        InitializeComponent();
        FormKeys.Attach(this);
    }
}
