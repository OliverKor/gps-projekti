using Gps.Core;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Gps.Ui.Wpf;

public partial class MainWindow : Window
{
    private IReadOnlyList<Fix> _fixes = Array.Empty<Fix>();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadAndRender();
    }

    private void LoadAndRender()
    {
        var csvPath = System.IO.Path.Combine(FindRepoRoot(), "track.csv");
        if (!File.Exists(csvPath))
        {
            Status.Text = $"CSV not found: {csvPath}";
            Fixes.ItemsSource = null;
            MapCanvas.Children.Clear();
            return;
        }

        _fixes = CsvFixReader.Read(csvPath);
        Fixes.ItemsSource = _fixes;
        Status.Text = $"Loaded {_fixes.Count} fixes from {csvPath}";

        MapCanvas.SizeChanged -= OnMapCanvasSizeChanged;
        MapCanvas.SizeChanged += OnMapCanvasSizeChanged;

        DrawTrack();
    }

    private void OnMapCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawTrack();
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

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var hasSolution = File.Exists(System.IO.Path.Combine(directory.FullName, "gps-projekti.slnx"));
            var hasGitDirectory = Directory.Exists(System.IO.Path.Combine(directory.FullName, ".git"));
            if (hasSolution || hasGitDirectory)
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
    }
}
