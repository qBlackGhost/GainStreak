using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GymApp.Desktop.ViewModels;
using GymApp.Services;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Brush = Mapsui.Styles.Brush;
using Color = Mapsui.Styles.Color;
using Pen = Mapsui.Styles.Pen;

namespace GymApp.Desktop.Views;

/// <summary>The map part lives here, since it's view-only: tiles, a marker per gym from the view model's list,
/// centring on the city, and turning a marker click into the same Pick as the list.</summary>
public partial class GymPickerView : UserControl
{
    private const double TapRadiusPixels = 18;
    // Names show once zoomed in past this resolution (map units per pixel; a whole city is ~30-40), so the centre
    // isn't a pile of overlapping labels. With only a few markers (e.g. after a search) they always show.
    private const double LabelMaxResolution = 12;
    private const int AlwaysLabelUpTo = 5;

    private static readonly Color Blue = Color.FromArgb(255, 70, 130, 180);
    private static readonly Color Green = Color.FromArgb(255, 46, 139, 87);

    private readonly MemoryLayer _gyms = new("Gyms") { Style = null };   // each marker carries its own style
    private GymPickerViewModel? _vm;
    private CityBox? _zoomedTo;

    public GymPickerView()
    {
        InitializeComponent();

        var map = new Map();
        var tiles = OpenStreetMap.CreateTileLayer(OsmHttp.UserAgent);   // the OSM tile policy asks for a real User-Agent
        // Tiles are the one part of the picker that needs the internet: say so instead of leaving a blank map
        tiles.DataChanged += (_, e) =>
        {
            var failed = e.Error is not null;
            Dispatcher.UIThread.Post(() => _vm?.MapTilesLoaded(ok: !failed));
        };
        map.Layers.Add(tiles);
        map.Layers.Add(_gyms);
        GymMap.Map = map;
        GymMap.MapTapped += OnMapTapped;
        GymMap.SizeChanged += (_, _) => ZoomToCityIfNeeded();   // the first time the map gets a size

        // The map zooms on the wheel / two-finger scroll but leaves the event unhandled, so it also bubbled up to
        // the page's ScrollViewer and the whole screen scrolled while zooming. The map's own handling runs first
        // (class handler); this then stops the event at the map.
        GymMap.AddHandler(PointerWheelChangedEvent, (_, e) => e.Handled = true, RoutingStrategies.Bubble,
            handledEventsToo: true);

        DataContextChanged += (_, _) => Attach(DataContext as GymPickerViewModel);
    }

    private void Attach(GymPickerViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.Gyms.CollectionChanged -= OnGymsChanged;
            _vm.PropertyChanged -= OnVmChanged;
        }
        _vm = vm;
        if (vm is null) return;
        vm.Gyms.CollectionChanged += OnGymsChanged;
        vm.PropertyChanged += OnVmChanged;
        UpdateMarkers();
    }

    private void OnGymsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateMarkers();

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GymPickerViewModel.ShowAsMap) or nameof(GymPickerViewModel.MapBox))
            // after the map is laid out, so the viewport has a size to fit the city into
            Dispatcher.UIThread.Post(ZoomToCityIfNeeded, DispatcherPriority.Background);
    }

    private void UpdateMarkers()
    {
        if (_vm is null) return;
        var placed = _vm.Gyms.Where(g => g.HasPosition).ToList();
        var alwaysLabel = placed.Count <= AlwaysLabelUpTo;
        _gyms.Features = placed.Select(g => Marker(g, alwaysLabel)).ToList();
        _gyms.FeaturesWereModified();
        GymMap.Map.RefreshData();
    }

    private static IFeature Marker(GymRowViewModel row, bool alwaysLabel)
    {
        var (x, y) = SphericalMercator.FromLonLat(row.Gym.Longitude!.Value, row.Gym.Latitude!.Value);
        var feature = new PointFeature(x, y) { Data = row };
        feature.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            SymbolScale = 0.5,
            Fill = new Brush { Color = row.IsCurrent ? Green : Blue },
            Outline = new Pen { Color = Color.White, Width = 2 }
        });
        feature.Styles.Add(new LabelStyle
        {
            Text = row.Name,
            Offset = new Offset(0, -18),
            Font = new Font { Size = 11 },
            ForeColor = Color.Black,
            BackColor = new Brush { Color = Color.FromArgb(220, 255, 255, 255) },
            CornerRounding = 4,
            CollisionDetection = true,   // hide overlapping names instead of piling them up
            MaxVisible = alwaysLabel ? double.MaxValue : LabelMaxResolution
        });
        return feature;
    }

    /// <summary>Fits the city's box when the map is first shown for a city; after that the user's pan and zoom stay.</summary>
    private void ZoomToCityIfNeeded()
    {
        if (_vm is not { ShowAsMap: true, MapBox: { } box } || box == _zoomedTo) return;
        if (GymMap.Bounds.Width <= 0) return;   // not laid out yet; SizeChanged tries again

        var (minX, minY) = SphericalMercator.FromLonLat(box.West, box.South);
        var (maxX, maxY) = SphericalMercator.FromLonLat(box.East, box.North);
        GymMap.Map.Navigator.ZoomToBox(new MRect(minX, minY, maxX, maxY), MBoxFit.Fit);
        _zoomedTo = box;
    }

    /// <summary>Picks the nearest marker within a finger's width of the click, if any.</summary>
    private void OnMapTapped(object? sender, MapEventArgs e)
    {
        if (_vm is null) return;
        var resolution = GymMap.Map.Navigator.Viewport.Resolution;   // map units per screen pixel
        var hit = _gyms.Features
            .OfType<PointFeature>()
            .Select(f => (Row: f.Data as GymRowViewModel, Pixels: f.Point.Distance(e.WorldPosition) / resolution))
            .Where(h => h.Row is not null && h.Pixels <= TapRadiusPixels)
            .OrderBy(h => h.Pixels)
            .FirstOrDefault();
        if (hit.Row is null) return;

        e.Handled = true;
        _vm.PickFromMap(hit.Row);
    }
}
