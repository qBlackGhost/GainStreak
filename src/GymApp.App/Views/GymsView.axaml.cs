using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
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
        MapCard.SizeChanged += (_, e) => { if (!_phone) LayOutMapHeader(e.NewSize.Width); };
        PageArea.SizeChanged += (_, _) => { if (_phone) LayOutPhone(true); };
        PhoneControls.SizeChanged += (_, _) => { if (_phone) LayOutPhone(true); };
        HookSheetDrag();
        EmptyNote.Transitions = [new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(400) }];

        // The map zooms on the wheel; stop the event there so the page doesn't scroll too (small windows)
        FriendMap.AddHandler(PointerWheelChangedEvent, (_, e) => e.Handled = true, RoutingStrategies.Bubble, handledEventsToo: true);
        // Phones (16f, map check 7): a drag or pinch on the map is the map's; the page around it mustn't scroll too
        // (on the way down, so the page's scroll gesture never sees them; Mapsui still gets them)
        if (GymApplication.Platform is PhonePlatform)
        {
            FriendMap.AddHandler(PointerPressedEvent, (_, e) => e.PreventGestureRecognition(), RoutingStrategies.Tunnel);
            FriendMap.AddHandler(PointerMovedEvent, (_, e) => e.PreventGestureRecognition(), RoutingStrategies.Tunnel);
            FriendMap.AddHandler(PointerReleasedEvent, (_, e) => e.PreventGestureRecognition(), RoutingStrategies.Tunnel);
        }

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
        LayOutPhone(MainView.IsPhoneLayout(this));
    }

    // ===== Phones (the GainStreak Mobile canvas) =====
    // The map fills the screen with the controls floating on it, and Your gym, Friends and Your check-ins are a sheet
    // from the bottom. The same controls move between the page and the phone layout, so nothing is built twice.

    private bool _phone;

    private void LayOutPhone(bool phone)
    {
        if (phone != _phone)
        {
            _phone = phone;
            Move(MapFrame, phone ? PhoneMap : MapCardGrid);
            Move(MapPills, phone ? PhonePills : MapHeader);
            Move(Chips, phone ? SettingsChips : Header);
            Move(RightColumn, phone ? PhoneSheetBody : Columns);
            if (!phone) LayOutMapHeader(MapCard.Bounds.Width);
            else { Grid.SetRow(MapPills, 0); Grid.SetColumn(MapPills, 0); MapPills.Margin = default; }
            MapFrame.CornerRadius = phone ? default : new CornerRadius(14);
            MapFrame.BorderThickness = phone ? default : new Thickness(1);
            PageScroll.IsVisible = !phone;
            PhoneMap.IsVisible = PhoneControls.IsVisible = PhoneSheet.IsVisible = phone;
            if (!phone) SettingsSheet.IsVisible = false;
            EmptyNote.HorizontalAlignment = phone ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
            StartNote();
            EmptyNote.MaxWidth = phone ? double.PositiveInfinity : 230;
            if (!phone)
            {
                ToastBox.Margin = new Thickness(16, 12, 16, 12);
                EmptyNote.Margin = new Thickness(12);
                LegendWrap.ClearValue(MarginProperty);
            }
        }
        // The range and mode pills: their own widths on the page; on a phone they share one row (range a bit wider)
        if (MapPills.Children is [Border range, Border mode])
        {
            var row = Math.Max(0, PageArea.Bounds.Width - 32 - MapPills.ItemSpacing);
            range.Width = phone ? Math.Floor(row * 0.57) : 234;
            mode.Width = phone ? Math.Floor(row * 0.43) : 184;
        }
        // The settings sheet's switches: side by side on a phone, half the row each
        Chips.ItemWidth = phone ? Math.Max(0, Math.Floor((PageArea.Bounds.Width - 32 - Chips.ItemSpacing) / 2)) : double.NaN;
        if (!phone) return;
        PhoneSheetBody.Width = Math.Max(0, PageArea.Bounds.Width - 32);
        SetSheet(_sheetOpen ? SheetOpenHeight : SheetClosedHeight, _sheetOpen ? Math.Min(_sheetOffset, SheetMaxOffset) : 0);
        // The toast, the note and the legend go just under the floating controls, the note across the map
        var top = PhoneControls.Margin.Top + PhoneControls.Bounds.Height + 10;
        ToastBox.Margin = new Thickness(16, top, 16, 0);
        EmptyNote.Margin = new Thickness(16, top, 16, 0);
        LegendWrap.Margin = new Thickness(4, top - 8, 0, 0);
    }

    // ===== The phones' sheet: no scrolling, it's dragged =====
    // Closed it shows Your gym only. Dragged up it grows to its whole content, at most the map's height;
    // past that the content itself follows the finger. Let go and it snaps open or closed; a tap on the grabber toggles it.

    /// <summary>The grabber's strip at the top of the sheet, and the body's space above and below.</summary>
    private const double SheetGrabber = 17, SheetTop = 4, SheetBottom = 24;
    /// <summary>A drag starts past this many pixels, so taps still reach the buttons in the sheet.</summary>
    private const double DragSlop = 8;

    private bool _sheetOpen, _dragging;
    private double _sheetOffset, _dragStartY, _dragStartExtent, _lastDy;
    private Avalonia.Input.IPointer? _dragPointer;

    private double SheetContentHeight => SheetGrabber + SheetTop + PhoneSheetBody.Bounds.Height + SheetBottom;
    private double SheetClosedHeight => Math.Min(SheetGrabber + SheetTop + YourGymCard.Bounds.Height + SheetBottom, SheetContentHeight);
    private double SheetOpenHeight => Math.Max(SheetClosedHeight, Math.Min(PageArea.Bounds.Height, SheetContentHeight));
    private double SheetMaxOffset => Math.Max(0, SheetContentHeight - SheetOpenHeight);

    private void SetSheet(double height, double offset)
    {
        PhoneSheet.Height = Math.Max(0, height);
        _sheetOffset = Math.Max(0, offset);
        Canvas.SetTop(PhoneSheetBody, SheetTop - _sheetOffset);
    }

    private void HookSheetDrag()
    {
        const RoutingStrategies tunnel = RoutingStrategies.Tunnel;
        PhoneSheet.AddHandler(PointerPressedEvent, OnSheetDown, tunnel, handledEventsToo: true);
        PhoneSheet.AddHandler(PointerMovedEvent, OnSheetMove, tunnel, handledEventsToo: true);
        PhoneSheet.AddHandler(PointerReleasedEvent, OnSheetUp, tunnel, handledEventsToo: true);
        PhoneSheet.AddHandler(PointerCaptureLostEvent, (_, e) => { if (e.Pointer == _dragPointer && _dragging) EndDrag(); }, tunnel, handledEventsToo: true);
        PhoneSheetBody.SizeChanged += (_, _) => { if (_phone && !_dragging) LayOutPhone(true); };
        // The gym card on the map stays above the sheet, whatever its height
        PhoneSheet.SizeChanged += (_, e) => { if (_phone) GymCard.Margin = new Thickness(16, 0, 16, e.NewSize.Height + 12); };
    }

    private void OnSheetDown(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        PhoneSheet.Transitions = null;   // follow the finger, no easing
        _dragPointer = e.Pointer;
        _dragging = false;
        _dragStartY = e.GetPosition(PageArea).Y;
        _dragStartExtent = PhoneSheet.Height + _sheetOffset;
        _lastDy = 0;
    }

    private void OnSheetMove(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (e.Pointer != _dragPointer || !e.GetCurrentPoint(PhoneSheet).Properties.IsLeftButtonPressed) return;
        var y = e.GetPosition(PageArea).Y;
        var dy = y - _dragStartY;
        if (!_dragging)
        {
            if (Math.Abs(dy) < DragSlop) return;
            _dragging = true;
            e.Pointer.Capture(PhoneSheet);   // the button under the finger lets go, so it doesn't click
        }
        var extent = Math.Clamp(_dragStartExtent - dy, SheetClosedHeight, SheetOpenHeight + SheetMaxOffset);
        _lastDy = (PhoneSheet.Height + _sheetOffset) - extent;   // > 0: moving down
        SetSheet(Math.Min(extent, SheetOpenHeight), extent - SheetOpenHeight);
        e.Handled = true;
    }

    private void OnSheetUp(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (e.Pointer != _dragPointer) return;
        if (_dragging)
        {
            e.Pointer.Capture(null);
            EndDrag();
            e.Handled = true;
        }
        else if (e.GetPosition(PhoneSheet).Y < SheetGrabber + 12)
            Snap(!_sheetOpen);
        _dragPointer = null;
    }

    private void EndDrag()
    {
        _dragging = false;
        if (_sheetOffset > 0) { _sheetOpen = true; return; }   // reading further down: stays open where it is
        var height = PhoneSheet.Height;
        var middle = (SheetClosedHeight + SheetOpenHeight) / 2;
        // The direction of the last move wins; a drag that stopped decides by where it ended
        Snap(_lastDy < -1 || (Math.Abs(_lastDy) <= 1 && height > middle));
    }

    private void Snap(bool open)
    {
        _sheetOpen = open;
        PhoneSheet.Transitions =
        [
            new Avalonia.Animation.DoubleTransition
            {
                Property = HeightProperty, Duration = TimeSpan.FromMilliseconds(220),
                Easing = new Avalonia.Animation.Easings.CubicEaseOut(),
            },
        ];
        SetSheet(open ? SheetOpenHeight : SheetClosedHeight, open ? _sheetOffset : 0);
    }

    private static void Move(Control control, Panel to)
    {
        if (control.Parent == to) return;
        (control.Parent as Panel)?.Children.Remove(control);
        to.Children.Add(control);
    }

    private void OnLegend(object? sender, RoutedEventArgs e)
    {
        var on = !LegendWrap.Classes.Contains("on");
        LegendWrap.Classes.Set("on", on);
        LegendButton.Classes.Set("on", on);
        ShowNote();
    }

    // ===== Phones: the empty-map note shows when the map opens, then fades =====

    private static readonly TimeSpan NoteShown = TimeSpan.FromSeconds(3);
    private bool _noteDone, _noteOn;
    private IDisposable? _noteTimer;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _noteDone = _noteOn = false;
        StartNote();
    }

    /// <summary>Once per visit, when there's a note to show: fade it in, and out again after <see cref="NoteShown"/>.</summary>
    private void StartNote()
    {
        if (!_phone || _noteDone || _vm is not { ShowMapNote: true }) { ShowNote(); return; }
        _noteDone = _noteOn = true;
        ShowNote();
        _noteTimer?.Dispose();
        _noteTimer = DispatcherTimer.RunOnce(() => { _noteOn = false; ShowNote(); }, NoteShown);
    }

    /// <summary>The note on the page always; on a phone only while it's fading in or out, and not under the legend.</summary>
    private void ShowNote() =>
        EmptyNote.Opacity = !_phone || (_noteOn && !LegendWrap.Classes.Contains("on")) ? 1 : 0;

    private void OnSettings(object? sender, RoutedEventArgs e) => SettingsSheet.IsVisible = true;
    private void OnSettingsDone(object? sender, RoutedEventArgs e) => SettingsSheet.IsVisible = false;

    private void OnSettingsScrim(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        SettingsSheet.IsVisible = false;
        e.Handled = true;
    }

    // A press inside the sheet mustn't reach the scrim behind it
    private void OnSheetPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e) => e.Handled = true;

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
        if (e.PropertyName == nameof(GymsViewModel.ShowMapNote)) StartNote();
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
        // Phones (16f, map check 9): zoom stops at a whole city out and street level in, so a pinch can't lose the map.
        // Resolutions are map metres per screen pixel (zoom levels 9 to 19). Set once the map has its city: set in the
        // constructor, before the map had a size, it hung the app. The desktop keeps its free zoom
        if (GymApplication.Platform is PhonePlatform)
            FriendMap.Map.Navigator.OverrideZoomBounds = new MMinMax(0.3, 305);
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
