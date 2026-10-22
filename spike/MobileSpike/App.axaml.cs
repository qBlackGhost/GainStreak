using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace MobileSpike;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        SpikeLog.Mark("Avalonia framework initialized");
        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new Window { Title = "GainStreak spike", Width = 420, Height = 860, Content = new MainView() };
                break;
            case IActivityApplicationLifetime activity:   // Android in Avalonia 12
                activity.MainViewFactory = () => new MainView();
                break;
            case ISingleViewApplicationLifetime single:   // iOS
                single.MainView = new MainView();
                break;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
