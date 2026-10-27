using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GymApp.Core;
using GymApp.App.ViewModels;
using GymApp.Services;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Brush = Mapsui.Styles.Brush;
using Color = Mapsui.Styles.Color;
using Font = Mapsui.Styles.Font;
using Pen = Mapsui.Styles.Pen;
using Size = Avalonia.Size;

namespace GymApp.App.Views;

/// <summary>
/// The Map screen's view-only parts: the fit-to-screen layout switch and the map itself (tiles, the city's gyms as
/// dots, friends as initial circles with a "+N" badge, your pin, tap to select). Rebuilt when the view model raises
/// <see cref="GymsViewModel.MapChanged"/>.
/// </summary>
public partial class GymsView : UserControl
{
    /// <summary>Below this window size the page scrolls and the columns stack.</summary>
    private const double FitMinWidth = 1100, FitMinHeight = 700;

    private const double TapRadiusPixels = 22;
    private const string GymIdKey = "GymId";

    private static readonly Color Accent = Color.FromArgb(255, 0xC6, 0xF4, 0x32);
    private static readonly Color DotGray = Color.FromArgb(255, 0x9A, 0xA1, 0xA7);
    private static readonly Color MapInk = Color.FromArgb(255, 0x0E, 0x11, 0x13);
    private static readonly Color AvatarFill = Color.FromArgb(255, 0x1E, 0x1E, 0x1E);

    private readonly MemoryLayer _dots = new("Gyms") { Style = null };          // each feature carries its own styles
    private readonly MemoryLayer _friends = new("FriendCheckIns") { Style = null };
    private readonly MemoryLayer _me = new("Me") { Style = null };
    private GymsViewModel? _vm;
    private MapBounds? _zoomedTo;

    public GymsView()
    {
        InitializeComponent();

        var map = new Map { BackColor = MapInk };
        var tiles = OpenStreetMap.CreateTileLayer(OsmHttp.UserAgent);   // the OSM tile policy asks for a real User-Agent
        tiles.Opacity = 0.6;    // the light OSM tiles, dimmed onto the dark background (the OSM credit is Mapsui's own)
        map.Layers.Add(tiles);
        map.Layers.Add(_dots);
        map.Layers.Add(_friends);   // added after the dots: drawn on top
        map.Layers.Add(_me);
        FriendMap.Map = map;
        FriendMap.MapTapped += OnMapTapped;
        FriendMap.SizeChanged += (_, _) => ZoomToCityIfNeeded();
        MapCard.SizeChanged += (_, e) => LayOutMapHeader(e.NewSize.Width);

        // The map zooms on the wheel; stop the event there so the page doesn't scroll too (small windows)
        FriendMap.AddHandler(PointerWheelChangedEvent, (_, e) => e.Handled = true, RoutingStrategies.Bubble, handledEventsToo: true);

        DataContextChanged += (_, _) => Attach(DataContext as GymsViewModel);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        LayOut(e.NewSize);
    }

    /// <summary>Large: the page fits the window, the map fills the left and the Friends card scrolls inside.
    /// Small: one column, the map at a fixed height, and the page scrolls.</summary>
    private void LayOut(Size size)
    {
        var fit = size.Width >= FitMinWidth && size.Height >= FitMinHeight;
        Columns.Classes.Set("stack", !fit);
        Header.Classes.Set("stack", size.Width < FitMinWidth);
        Columns.ColumnDefinitions = fit ? new ColumnDefinitions("1.6*,20,*") : new ColumnDefinitions("*");
        Columns.RowDefinitions = fit ? new RowDefinitions("*") : new RowDefinitions("Auto,Auto");
        PageScroll.VerticalScrollBarVisibility = fit ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
    }

    /// <summary>Below this card width the range and mode pills go under the title instead of beside it.</summary>
    private const double MapHeaderMinWidth = 640;

    private void LayOutMapHeader(double cardWidth)
    {
        var stacked = cardWidth < MapHeaderMinWidth;
        Grid.SetRow(MapPills, stacked ? 1 : 0);
        Grid.SetColumn(MapPills, stacked ? 0 : 1);
        Grid.SetColumnSpan(MapPills, stacked ? 2 : 1);
        MapPills.Margin = stacked ? new Thickness(0, 10, 0, 0) : default;
    }

    private void Attach(GymsViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.MapChanged -= OnMapChanged;
            _vm.PropertyChanged -= OnVmChanged;
        }
        _vm = vm;
        if (vm is null) return;
        vm.MapChanged += OnMapChanged;
        vm.PropertyChanged += OnVmChanged;
        Redraw();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GymsViewModel.Bounds))
            Dispatcher.UIThread.Post(ZoomToCityIfNeeded, DispatcherPriority.Background);
    }

    private int? _centeredOn;

    private void OnMapChanged(object? sender, EventArgs e)
    {
        Redraw();
        // A Friends row picked a gym: bring it into view
        if (_vm?.SelectedGymId is { } id && id != _centeredOn) CenterOn(id);
        _centeredOn = _vm?.SelectedGymId;
    }

    private void Redraw()
    {
        if (_vm is null) return;
        var selected = _vm.SelectedGymId;
        _dots.Features = _vm.GymDots.Select(d => Dot(d, d.Id == selected)).ToList();
        _friends.Features = _vm.Pins.Select(p => Pin(p, p.GymId == selected)).ToList();
        _me.Features = _vm.MyPin is { } me ? [MyPin(me)] : [];
        foreach (var layer in new[] { _dots, _friends, _me }) layer.FeaturesWereModified();
        FriendMap.Map.RefreshData();
        ZoomToCityIfNeeded();
    }

    private static PointFeature At(double lat, double lon, int gymId)
    {
        var (x, y) = SphericalMercator.FromLonLat(lon, lat);
        var f = new PointFeature(x, y);
        f[GymIdKey] = gymId;
        return f;
    }

    /// <summary>A gym: a small neutral dot; your gym white, today's gym accent. Selected: a ring.</summary>
    private static IFeature Dot(MapGymDot d, bool selected)
    {
        var f = At(d.Latitude, d.Longitude, d.Id);
        var fill = d.IsToday ? Accent : d.IsHome ? Color.White : DotGray;
        f.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            SymbolScale = d.IsHome || d.IsToday || selected ? 0.45 : 0.35,
            Fill = new Brush { Color = fill },
            Outline = new Pen { Color = selected ? Color.White : MapInk, Width = selected ? 3 : 2 }
        });
        return f;
    }

    /// <summary>Friends at a gym: the newest friend's initials in a circle, "+N" for more, faded by age.</summary>
    private static IFeature Pin(GymPin pin, bool selected)
    {
        var f = At(pin.Latitude, pin.Longitude, pin.GymId);
        var opacity = (float)FriendMapRules.Opacity(pin.NewestDaysAgo);
        f.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            SymbolScale = selected ? 1.0 : 0.85,
            Fill = new Brush { Color = AvatarFill },
            Outline = new Pen { Color = selected ? Accent : Color.White, Width = selected ? 3 : 2 },
            Opacity = opacity
        });
        f.Styles.Add(new LabelStyle
        {
            Text = FriendMapRules.Initials(pin.Newest.Username),
            ForeColor = Color.White,
            BackColor = null,
            Font = new Font { Size = 12, Bold = true },
            Opacity = opacity
        });
        if (pin.MoreCount > 0)
            f.Styles.Add(new LabelStyle
            {
                Text = $"+{pin.MoreCount}",
                ForeColor = Color.Black,
                BackColor = new Brush { Color = Color.White },
                CornerRounding = 8,
                Font = new Font { Size = 10, Bold = true },
                Offset = new Offset(17, -15)
            });
        return f;
    }

    /// <summary>You, at today's gym: your initials on the accent colour.</summary>
    private static IFeature MyPin(MapMyPin me)
    {
        var f = At(me.Latitude, me.Longitude, me.GymId);
        f.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            SymbolScale = 0.85,
            Fill = new Brush { Color = Accent },
            Outline = new Pen { Color = Color.Black, Width = 2 }
        });
        f.Styles.Add(new LabelStyle
        {
            Text = me.Initials,
            ForeColor = Color.Black,
            BackColor = null,
            Font = new Font { Size = 12, Bold = true }
        });
        return f;
    }

    /// <summary>Fits the home city the first time the map has a size for it; after that the user's pan and zoom stay.</summary>
    private void ZoomToCityIfNeeded()
    {
        if (_vm?.Bounds is not { } b || b == _zoomedTo) return;
        if (FriendMap.Bounds.Width <= 0) return;   // not laid out yet; SizeChanged tries again
        var (minX, minY) = SphericalMercator.FromLonLat(b.West, b.South);
        var (maxX, maxY) = SphericalMercator.FromLonLat(b.East, b.North);
        FriendMap.Map.Navigator.ZoomToBox(new MRect(minX, minY, maxX, maxY), MBoxFit.Fit);
        _zoomedTo = b;
    }

    private void CenterOn(int gymId)
    {
        if (_vm is null) return;
        var spot = _vm.Pins.Where(p => p.GymId == gymId).Select(p => (p.Latitude, p.Longitude))
            .Concat(_vm.GymDots.Where(d => d.Id == gymId).Select(d => (d.Latitude, d.Longitude)))
            .Cast<(double Lat, double Lon)?>().FirstOrDefault();
        if (spot is not { } s) return;
        var (x, y) = SphericalMercator.FromLonLat(s.Lon, s.Lat);
        FriendMap.Map.Navigator.CenterOn(new MPoint(x, y));
    }

    /// <summary>Selects the nearest pin or gym dot within a finger's width of the click (pins win over dots).</summary>
    private void OnMapTapped(object? sender, MapEventArgs e)
    {
        if (_vm is null) return;
        var resolution = FriendMap.Map.Navigator.Viewport.Resolution;   // map units per screen pixel
        IEnumerable<IFeature> candidates = _friends.Features.Concat(_me.Features).Concat(_dots.Features);
        var hit = candidates
            .OfType<PointFeature>()
            .Select((f, order) => (Id: f[GymIdKey] as int?, Pixels: f.Point.Distance(e.WorldPosition) / resolution, Order: order))
            .Where(h => h.Id is not null && h.Pixels <= TapRadiusPixels)
            .OrderBy(h => h.Pixels > 8 ? 1 : 0).ThenBy(h => h.Order).ThenBy(h => h.Pixels)
            .FirstOrDefault();
        if (hit.Id is not { } id) return;

        e.Handled = true;
        _centeredOn = id;   // tapped on the map: it's already in view
        _vm.SelectGym(id);
    }
}
