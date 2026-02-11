using Gps.Core;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Gps.Ui.Wpf;

public partial class MainWindow : Window
{
    private const int MaxFixCount = 5000;
    private static readonly int[] BaudRateOptions = [9600, 19200, 38400, 57600, 115200];
    private readonly ObservableCollection<Fix> _fixes = [];
    private LiveGpsSession? _session;
    private bool _isConnected;

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

    private void OnMapCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawTrack();
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        await StopSessionAsync();
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
        var csvPath = Path.Combine(AppContext.BaseDirectory, "track.csv");

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
        SetStatus($"Connected to {portName} @ {baudRate}. CSV logging: {loggingText}.");
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
            Status.Text = $"Connected. Last fix {fix.Timestamp:o}. Total fixes: {_fixes.Count}.";
        });
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

        if (_fixes.Count < 2 || MapCanvas.ActualWidth <= 0 || MapCanvas.ActualHeight <= 0)
        {
            return;
        }

        var minLon = _fixes[0].LongitudeDeg;
        var maxLon = _fixes[0].LongitudeDeg;
        var minLat = _fixes[0].LatitudeDeg;
        var maxLat = _fixes[0].LatitudeDeg;

        for (var i = 1; i < _fixes.Count; i++)
        {
            var fix = _fixes[i];
            if (fix.LongitudeDeg < minLon) minLon = fix.LongitudeDeg;
            if (fix.LongitudeDeg > maxLon) maxLon = fix.LongitudeDeg;
            if (fix.LatitudeDeg < minLat) minLat = fix.LatitudeDeg;
            if (fix.LatitudeDeg > maxLat) maxLat = fix.LatitudeDeg;
        }

        var lonSpan = maxLon - minLon;
        var latSpan = maxLat - minLat;
        if (lonSpan <= 0 || latSpan <= 0)
        {
            return;
        }

        const double padding = 10;
        var drawWidth = MapCanvas.ActualWidth - (2 * padding);
        var drawHeight = MapCanvas.ActualHeight - (2 * padding);

        var path = new Polyline
        {
            Stroke = Brushes.Lime,
            StrokeThickness = 2
        };

        foreach (var fix in _fixes)
        {
            var x = ((fix.LongitudeDeg - minLon) / lonSpan) * drawWidth + padding;
            var y = ((maxLat - fix.LatitudeDeg) / latSpan) * drawHeight + padding;
            path.Points.Add(new Point(x, y));
        }

        MapCanvas.Children.Add(path);
    }
}
