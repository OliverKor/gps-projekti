using Gps.Core;
using System;
using System.IO;
using System.Windows;
using System.Windows.Shapes;
using System.Windows.Media;
using System.Linq;
using static System.Net.WebRequestMethods;

namespace Gps.Ui.Wpf;

public partial class MainWindow : Window
{
    private IReadOnlyList<Fix> _fixes = Array.Empty<Fix>();

    public MainWindow()
    {
        InitializeComponent();

        var repoRoot = FindRepoRoot();
        var csvPath = System.IO.Path.Combine(repoRoot, "track.csv"); // nyt repojuuressa

        _fixes = System.IO.File.Exists(csvPath)
            ? CsvFixReader.Read(csvPath)
            : Array.Empty<Fix>();

        Status.Text = System.IO.File.Exists(csvPath)
            ? $"Loaded {_fixes.Count} fixes from {csvPath}"
            : $"CSV not found: {csvPath}";

        Fixes.ItemsSource = _fixes;
        
        Loaded += (s, e) => 
        {
            MapCanvas.SizeChanged += (sc, se) => DrawTrack();
            DrawTrack();
        };
    }

    private void DrawTrack()
    {
        MapCanvas.Children.Clear();

        // Edge case: less than 2 points or no canvas size
        if (_fixes.Count < 2 || MapCanvas.ActualWidth <= 0 || MapCanvas.ActualHeight <= 0)
            return;

        double minLon = _fixes.Min(f => f.LongitudeDeg);
        double maxLon = _fixes.Max(f => f.LongitudeDeg);
        double minLat = _fixes.Min(f => f.LatitudeDeg);
        double maxLat = _fixes.Max(f => f.LatitudeDeg);

        double lonSpan = maxLon - minLon;
        double latSpan = maxLat - minLat;

        // Edge case: zero span (all points at same location)
        if (lonSpan == 0 || latSpan == 0)
            return;

        const double pad = 10;
        double canvasWidth = MapCanvas.ActualWidth;
        double canvasHeight = MapCanvas.ActualHeight;

        var polyline = new Polyline
        {
            Stroke = Brushes.Lime,
            StrokeThickness = 2
        };

        foreach (var fix in _fixes)
        {
            double x = (fix.LongitudeDeg - minLon) / lonSpan * (canvasWidth - 2 * pad) + pad;
            double y = (maxLat - fix.LatitudeDeg) / latSpan * (canvasHeight - 2 * pad) + pad;
            polyline.Points.Add(new Point(x, y));
        }

        MapCanvas.Children.Add(polyline);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir != null)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "gps-projekti.slnx")) ||
                Directory.Exists(System.IO.Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        return AppContext.BaseDirectory;
    }
}
