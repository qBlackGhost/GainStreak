using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using GymApp.Services;
using Mapsui;
using Mapsui.Projections;
using Mapsui.Tiling;
using Mapsui.UI.Avalonia;

namespace MobileSpike;

/// <summary>The results of the four checks on top, the map below. Drawn at once; the checks run after the first frame.</summary>
public class MainView : UserControl
{
    private readonly TextBlock _results = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, FontFamily = FontFamily.Parse("Menlo, monospace") };
    private bool _started;

    public MainView()
    {
        var map = new Map();
        map.Layers.Add(OpenStreetMap.CreateTileLayer(OsmHttp.UserAgent));
        var (x, y) = SphericalMercator.FromLonLat(26.1025, 44.4268);   // Bucharest
        map.Navigator.CenterOnAndZoomTo(new MPoint(x, y), map.Navigator.Resolutions[12]);

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var header = new ScrollViewer
        {
            MaxHeight = 380,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(12, 48, 12, 8),
                Spacing = 6,
                Children = { new TextBlock { Text = "GainStreak 16a spike", FontSize = 18, FontWeight = FontWeight.Bold }, _results }
            }
        };
        grid.Children.Add(header);
        var mapControl = new MapControl { Map = map };
        Grid.SetRow(mapControl, 1);
        grid.Children.Add(mapControl);
        Content = grid;
        _results.Text = "Running…";
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_started) return;
        _started = true;
        // The frame after this one is the first one on screen
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(_ =>
        {
            SpikeLog.FirstFrame();
            Task.Run(async () =>
            {
                await SpikeRunner.RunAsync(line => Dispatcher.UIThread.Post(() => _results.Text = SpikeLog.Text));
                Dispatcher.UIThread.Post(() => _results.Text = SpikeLog.Text);
            });
        });
    }
}
