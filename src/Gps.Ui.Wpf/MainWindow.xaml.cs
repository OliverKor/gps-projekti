using Gps.Core;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Logging;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Providers;
using Mapsui.Styles;
using Mapsui.Tiling;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MapsuiBrush = Mapsui.Styles.Brush;
using MapsuiColor = Mapsui.Styles.Color;
using MapsuiPen = Mapsui.Styles.Pen;
using NtsCoordinate = NetTopologySuite.Geometries.Coordinate;
using NtsLineString = NetTopologySuite.Geometries.LineString;

namespace Gps.Ui.Wpf;

public partial class MainWindow : Window
{
    private const int MaxFixCount = 5000;
    private const double MapPadding = 10;
    private const double MinimumAxisSpanMeters = 0.01;
    private const double StartMarkerDiameter = 8;
    private const double LatestMarkerDiameter = 10;
    private const string RealMapModeLabel = "Real map";
    private const string LocalXyModeLabel = "Local XY";
    private const string DefaultTileUserAgent = "gps-projekti/1.0";
    private const string DefaultMapCrs = "EPSG:3857";
    private const string Wgs84Crs = "EPSG:4326";
    private const int TileErrorFallbackThreshold = 8;
    private static readonly TimeSpan TileErrorWindow = TimeSpan.FromSeconds(30);
    private static readonly string[] TileFailureKeywords = ["tile", "http", "network", "socket", "timeout", "openstreetmap"];
    private static readonly int[] BaudRateOptions = [9600, 19200, 38400, 57600, 115200];

    private readonly ObservableCollection<Fix> _fixes = [];
    private readonly Queue<DateTimeOffset> _tileErrorTimestamps = new();

    private LiveGpsSession? _session;
    private Mapsui.Map? _realMap;
    private Layer? _trackLayer;
    private Layer? _latestLayer;

    private bool _isConnected;
    private bool _allowWindowClose;
    private bool _hasCenteredRealMap;
    private bool _hasTileFallbackTriggered;
    private bool _isUpdatingMapModeSelection;

    private int _lastPlottedPointCount;
    private double _lastSpanXmeters;
    private double _lastSpanYmeters;

    private Action<LogLevel, string, Exception?>? _previousMapLogDelegate;
    private Action<LogLevel, string, Exception?>? _installedMapLogDelegate;

    private MapMode _currentMapMode = MapMode.RealMap;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Fixes.ItemsSource = _fixes;
        BaudRateCombo.ItemsSource = BaudRateOptions;
        BaudRateCombo.SelectedItem = 38400;
        MapCanvas.SizeChanged += OnMapCanvasSizeChanged;

        InitializeRealMap();

        _isUpdatingMapModeSelection = true;
        MapModeCombo.SelectedIndex = 0;
        _isUpdatingMapModeSelection = false;
        SetMapMode(MapMode.RealMap, updateSelector: false);

        RefreshPorts(shouldUpdateStatus: false);
        if (PortCombo.SelectedItem is null)
        {
            SetStatus("No serial ports found.");
        }
        else
        {
            SetStatus("Disconnected.");
        }

        UpdateUiState();
    }

    private void InitializeRealMap()
    {
        _realMap = new Mapsui.Map
        {
            CRS = DefaultMapCrs
        };

        var tileLayer = OpenStreetMap.CreateTileLayer(DefaultTileUserAgent);
        _realMap.Layers.Add(tileLayer);

        _trackLayer = new Layer("Live track")
        {
            Style = new VectorStyle
            {
                Line = new MapsuiPen(MapsuiColor.Lime, 3)
            }
        };

        _latestLayer = new Layer("Latest fix")
        {
            Style = new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse,
                Fill = new MapsuiBrush(MapsuiColor.DeepSkyBlue),
                Outline = new MapsuiPen(MapsuiColor.White, 2),
                SymbolScale = 0.9,
                UnitType = UnitType.Pixel
            }
        };

        _realMap.Layers.Add(_trackLayer);
        _realMap.Layers.Add(_latestLayer);

        RealMapControl.Map = _realMap;
        UpdateRealMapOverlays();
        AttachMapLogging();
    }

    private void OnMapCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawTrack();
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowWindowClose)
        {
            return;
        }

        e.Cancel = true;
        await StopSessionAsync();
        DetachMapLogging();
        _allowWindowClose = true;
        Close();
    }

    private void RefreshPortsButton_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshPorts(shouldUpdateStatus: true);
    }

    private void RefreshPorts(bool shouldUpdateStatus)
    {
        var ports = SerialPort.GetPortNames()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var previousSelection = PortCombo.SelectedItem as string;
        PortCombo.ItemsSource = ports;

        if (ports.Length == 0)
        {
            PortCombo.SelectedItem = null;
            if (shouldUpdateStatus)
            {
                SetStatus("No serial ports found.");
            }

            UpdateUiState();
            return;
        }

        PortCombo.SelectedItem = previousSelection is not null && ports.Contains(previousSelection, StringComparer.OrdinalIgnoreCase)
            ? previousSelection
            : ports[0];

        if (shouldUpdateStatus && !_isConnected)
        {
            SetStatus($"Found {ports.Length} port(s).");
        }

        UpdateUiState();
    }

    private void ConnectButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isConnected)
        {
            return;
        }

        if (PortCombo.SelectedItem is not string portName || string.IsNullOrWhiteSpace(portName))
        {
            SetStatus("Select a COM port first.");
            return;
        }

        if (BaudRateCombo.SelectedItem is not int baudRate)
        {
            SetStatus("Select a baud rate first.");
            return;
        }

        var logToCsv = LogToCsvCheckBox.IsChecked == true;
        var csvPath = System.IO.Path.Combine(AppContext.BaseDirectory, "track.csv");

        ResetSessionViewState();

        var session = new LiveGpsSession(portName, baudRate, logToCsv, csvPath);
        session.FixReceived += OnFixReceived;
        session.Error += OnSessionError;
        session.Stopped += OnSessionStopped;

        try
        {
            session.Start();
        }
        catch (Exception ex)
        {
            session.FixReceived -= OnFixReceived;
            session.Error -= OnSessionError;
            session.Stopped -= OnSessionStopped;
            session.Dispose();
            SetStatus($"Failed to open serial port: {ex.Message}");
            return;
        }

        _session = session;
        _isConnected = true;
        UpdateUiState();

        var loggingText = logToCsv ? $"ON ({csvPath})" : "OFF";
        SetStatus($"Connected to {portName} @ {baudRate}. Map mode: {GetCurrentMapModeLabel()}. CSV logging: {loggingText}.");
    }

    private void ResetSessionViewState()
    {
        _fixes.Clear();
        _hasCenteredRealMap = false;
        ResetTileErrorTracking(clearFallbackFlag: true);
        DrawTrack();
        UpdateRealMapOverlays();
    }

    private async void DisconnectButton_OnClick(object sender, RoutedEventArgs e)
    {
        await StopSessionAsync();
        SetStatus("Disconnected.");
    }

    private async Task StopSessionAsync()
    {
        var session = _session;
        if (session is null)
        {
            _isConnected = false;
            UpdateUiState();
            return;
        }

        _session = null;
        _isConnected = false;
        UpdateUiState();

        session.FixReceived -= OnFixReceived;
        session.Error -= OnSessionError;
        session.Stopped -= OnSessionStopped;

        await session.StopAsync();
        session.Dispose();
    }

    private void OnFixReceived(object? sender, Fix fix)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            _fixes.Add(fix);
            while (_fixes.Count > MaxFixCount)
            {
                _fixes.RemoveAt(0);
            }

            DrawTrack();
            UpdateRealMapOverlays();
            FollowRealMapIfEnabled(fix);
            Status.Text = BuildConnectedStatus(fix);
        });
    }

    private string BuildConnectedStatus(Fix fix)
    {
        var fallbackText = _hasTileFallbackTriggered ? " Tile errors detected; using Local XY." : string.Empty;
        return $"Connected. Last fix {fix.Timestamp:o}. Total fixes: {_fixes.Count}. Mode: {GetCurrentMapModeLabel()}. " +
               $"Map points: {_lastPlottedPointCount}. Span: {_lastSpanXmeters:F1} m x {_lastSpanYmeters:F1} m.{fallbackText}";
    }

    private string GetCurrentMapModeLabel()
    {
        return _currentMapMode == MapMode.RealMap ? RealMapModeLabel : LocalXyModeLabel;
    }

    private void FollowRealMapIfEnabled(Fix latestFix)
    {
        if (_currentMapMode != MapMode.RealMap || AutoFollowCheckBox.IsChecked != true || _realMap is null)
        {
            return;
        }

        var (x, y) = SphericalMercator.FromLonLat(latestFix.LongitudeDeg, latestFix.LatitudeDeg);
        var projectedPoint = new MPoint(x, y);

        if (!_hasCenteredRealMap)
        {
            var initialResolution = GetInitialRealMapResolution(_realMap);
            _realMap.Navigator.CenterOnAndZoomTo(projectedPoint, initialResolution, 0, null);
            _hasCenteredRealMap = true;
            return;
        }

        _realMap.Navigator.CenterOn(projectedPoint, 0, null);
    }

    private static double GetInitialRealMapResolution(Mapsui.Map map)
    {
        var resolutions = map.Navigator.Resolutions;
        if (resolutions.Count == 0)
        {
            return map.Navigator.Viewport.Resolution;
        }

        var initialLevel = Math.Min(15, resolutions.Count - 1);
        return resolutions[initialLevel];
    }

    private void UpdateRealMapOverlays()
    {
        if (_trackLayer is null || _latestLayer is null || _realMap is null)
        {
            return;
        }

        var trackFeatures = new List<IFeature>();
        if (_fixes.Count >= 2)
        {
            trackFeatures.Add(CreateTrackFeature());
        }

        var latestFeatures = new List<IFeature>();
        if (_fixes.Count > 0)
        {
            var latest = _fixes[^1];
            latestFeatures.Add(new PointFeature(latest.LongitudeDeg, latest.LatitudeDeg));
        }

        var targetCrs = _realMap.CRS ?? DefaultMapCrs;
        _trackLayer.DataSource = CreateProjectedProvider(trackFeatures, targetCrs);
        _latestLayer.DataSource = CreateProjectedProvider(latestFeatures, targetCrs);

        _realMap.Refresh(ChangeType.Discrete);
        RealMapControl.RefreshGraphics();
    }

    private static IProvider CreateProjectedProvider(IEnumerable<IFeature> features, string targetCrs)
    {
        var sourceProvider = new MemoryProvider(features.ToList())
        {
            CRS = Wgs84Crs
        };

        return new ProjectingProvider(sourceProvider, new Projection())
        {
            CRS = targetCrs
        };
    }

    private GeometryFeature CreateTrackFeature()
    {
        var coordinates = new NtsCoordinate[_fixes.Count];
        for (var i = 0; i < _fixes.Count; i++)
        {
            var fix = _fixes[i];
            coordinates[i] = new NtsCoordinate(fix.LongitudeDeg, fix.LatitudeDeg);
        }

        var line = new NtsLineString(coordinates)
        {
            SRID = 4326
        };

        return new GeometryFeature(line);
    }

    private void MapModeCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingMapModeSelection)
        {
            return;
        }

        var mode = MapModeCombo.SelectedIndex == 1 ? MapMode.LocalXy : MapMode.RealMap;
        if (mode == MapMode.RealMap)
        {
            ResetTileErrorTracking(clearFallbackFlag: true);
        }

        SetMapMode(mode, updateSelector: false);
    }

    private void SetMapMode(MapMode mode, bool updateSelector = true)
    {
        _currentMapMode = mode;
        var isRealMap = mode == MapMode.RealMap;

        RealMapControl.Visibility = isRealMap ? Visibility.Visible : Visibility.Collapsed;
        MapCanvas.Visibility = isRealMap ? Visibility.Collapsed : Visibility.Visible;
        MapAttribution.Visibility = isRealMap ? Visibility.Visible : Visibility.Collapsed;

        if (updateSelector)
        {
            _isUpdatingMapModeSelection = true;
            MapModeCombo.SelectedIndex = isRealMap ? 0 : 1;
            _isUpdatingMapModeSelection = false;
        }

        if (isRealMap)
        {
            UpdateRealMapOverlays();
            if (_fixes.Count > 0)
            {
                FollowRealMapIfEnabled(_fixes[^1]);
            }
        }
        else
        {
            DrawTrack();
        }
    }

    private void AttachMapLogging()
    {
        if (_installedMapLogDelegate is not null)
        {
            return;
        }

        _previousMapLogDelegate = Logger.LogDelegate;
        _installedMapLogDelegate = (level, message, exception) =>
        {
            _previousMapLogDelegate?.Invoke(level, message, exception);
            OnMapLog(level, message, exception);
        };

        Logger.LogDelegate = _installedMapLogDelegate;
    }

    private void DetachMapLogging()
    {
        if (_installedMapLogDelegate is not null && ReferenceEquals(Logger.LogDelegate, _installedMapLogDelegate))
        {
            Logger.LogDelegate = _previousMapLogDelegate;
        }

        _installedMapLogDelegate = null;
        _previousMapLogDelegate = null;
    }

    private void OnMapLog(LogLevel level, string message, Exception? exception)
    {
        if (level != LogLevel.Error || _currentMapMode != MapMode.RealMap || !LooksLikeTileFailure(message, exception))
        {
            return;
        }

        var shouldFallback = false;
        lock (_tileErrorTimestamps)
        {
            var now = DateTimeOffset.UtcNow;
            var minAcceptedTimestamp = now - TileErrorWindow;
            _tileErrorTimestamps.Enqueue(now);

            while (_tileErrorTimestamps.Count > 0 && _tileErrorTimestamps.Peek() < minAcceptedTimestamp)
            {
                _tileErrorTimestamps.Dequeue();
            }

            if (!_hasTileFallbackTriggered && _tileErrorTimestamps.Count >= TileErrorFallbackThreshold)
            {
                _hasTileFallbackTriggered = true;
                shouldFallback = true;
            }
        }

        if (!shouldFallback)
        {
            return;
        }

        _ = Dispatcher.InvokeAsync(() =>
        {
            SetMapMode(MapMode.LocalXy);
            SetStatus("Real map tile loading failed repeatedly. Automatically switched to Local XY mode.");
        });
    }

    private static bool LooksLikeTileFailure(string message, Exception? exception)
    {
        var combined = $"{message} {exception?.Message}".ToLowerInvariant();
        return TileFailureKeywords.Any(combined.Contains);
    }

    private void ResetTileErrorTracking(bool clearFallbackFlag)
    {
        lock (_tileErrorTimestamps)
        {
            _tileErrorTimestamps.Clear();
        }

        if (clearFallbackFlag)
        {
            _hasTileFallbackTriggered = false;
        }
    }

    private void OnSessionError(object? sender, string message)
    {
        _ = Dispatcher.InvokeAsync(async () =>
        {
            await StopSessionAsync();
            SetStatus(message);
        });
    }

    private void OnSessionStopped(object? sender, EventArgs e)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            _isConnected = false;
            UpdateUiState();
        });
    }

    private void UpdateUiState()
    {
        var hasPorts = PortCombo.SelectedItem is not null;
        ConnectButton.IsEnabled = !_isConnected && hasPorts;
        DisconnectButton.IsEnabled = _isConnected;
        PortCombo.IsEnabled = !_isConnected;
        RefreshPortsButton.IsEnabled = !_isConnected;
        BaudRateCombo.IsEnabled = !_isConnected;
        LogToCsvCheckBox.IsEnabled = !_isConnected;
    }

    private void SetStatus(string message)
    {
        Status.Text = message;
    }

    private void DrawTrack()
    {
        MapCanvas.Children.Clear();
        _lastPlottedPointCount = 0;
        _lastSpanXmeters = 0;
        _lastSpanYmeters = 0;

        if (MapCanvas.ActualWidth <= 0 || MapCanvas.ActualHeight <= 0)
        {
            return;
        }

        var plottedFixes = CollectPlottedFixes();
        if (plottedFixes.Count == 0)
        {
            return;
        }

        _lastPlottedPointCount = plottedFixes.Count;

        if (plottedFixes.Count == 1)
        {
            var center = new Point(MapCanvas.ActualWidth / 2.0, MapCanvas.ActualHeight / 2.0);
            AddMarker(center, LatestMarkerDiameter, Brushes.DeepSkyBlue, Brushes.White);
            return;
        }

        var drawWidth = MapCanvas.ActualWidth - (2 * MapPadding);
        var drawHeight = MapCanvas.ActualHeight - (2 * MapPadding);
        if (drawWidth <= 0 || drawHeight <= 0)
        {
            return;
        }

        var minX = plottedFixes[0].XMeters;
        var maxX = plottedFixes[0].XMeters;
        var minY = plottedFixes[0].YMeters;
        var maxY = plottedFixes[0].YMeters;

        for (var i = 1; i < plottedFixes.Count; i++)
        {
            var plottedFix = plottedFixes[i];
            if (plottedFix.XMeters < minX) minX = plottedFix.XMeters;
            if (plottedFix.XMeters > maxX) maxX = plottedFix.XMeters;
            if (plottedFix.YMeters < minY) minY = plottedFix.YMeters;
            if (plottedFix.YMeters > maxY) maxY = plottedFix.YMeters;
        }

        _lastSpanXmeters = maxX - minX;
        _lastSpanYmeters = maxY - minY;

        var xSpan = Math.Max(_lastSpanXmeters, MinimumAxisSpanMeters);
        var ySpan = Math.Max(_lastSpanYmeters, MinimumAxisSpanMeters);
        var scaleX = drawWidth / xSpan;
        var scaleY = drawHeight / ySpan;
        var scale = Math.Min(scaleX, scaleY);

        var scaledWidth = xSpan * scale;
        var scaledHeight = ySpan * scale;
        var offsetX = MapPadding + ((drawWidth - scaledWidth) / 2.0);
        var offsetY = MapPadding + ((drawHeight - scaledHeight) / 2.0);

        var path = new Polyline
        {
            Stroke = Brushes.Lime,
            StrokeThickness = 2
        };

        foreach (var plottedFix in plottedFixes)
        {
            var x = ((plottedFix.XMeters - minX) * scale) + offsetX;
            var y = ((maxY - plottedFix.YMeters) * scale) + offsetY;
            path.Points.Add(new Point(x, y));
        }

        MapCanvas.Children.Add(path);

        if (path.Points.Count == 0)
        {
            return;
        }

        AddMarker(path.Points[0], StartMarkerDiameter, Brushes.Gold, Brushes.Black);
        AddMarker(path.Points[^1], LatestMarkerDiameter, Brushes.DeepSkyBlue, Brushes.White);
    }

    private List<PlottedFix> CollectPlottedFixes()
    {
        var plottedFixes = new List<PlottedFix>(_fixes.Count);
        foreach (var fix in _fixes)
        {
            if (!fix.LongitudeMeters.HasValue || !fix.LatitudeMeters.HasValue)
            {
                continue;
            }

            plottedFixes.Add(new PlottedFix(fix.LongitudeMeters.Value, fix.LatitudeMeters.Value));
        }

        return plottedFixes;
    }

    private void AddMarker(System.Windows.Point point, double diameter, System.Windows.Media.Brush fill, System.Windows.Media.Brush stroke)
    {
        var marker = new Ellipse
        {
            Width = diameter,
            Height = diameter,
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = 1.5
        };

        Canvas.SetLeft(marker, point.X - (diameter / 2.0));
        Canvas.SetTop(marker, point.Y - (diameter / 2.0));
        MapCanvas.Children.Add(marker);
    }

    private readonly record struct PlottedFix(double XMeters, double YMeters);

    private enum MapMode
    {
        RealMap,
        LocalXy
    }
}
