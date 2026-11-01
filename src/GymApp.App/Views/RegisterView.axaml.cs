using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GymApp.Core;

namespace GymApp.App.Views;

public partial class RegisterView : UserControl
{
    public RegisterView()
    {
        InitializeComponent();
        FormKeys.Attach(this);
    }

    private async void OnOpenTerms(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (TopLevel.GetTopLevel(this) is { } top) await top.Launcher.LaunchUriAsync(new Uri(Credentials.TermsUrl));
        }
        catch (Exception) { }   // no browser to open: nothing to do here
    }
}
