using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Aion2DpsMeter.Core.Combat;

namespace Aion2DpsMeter.App;

/// <summary>
/// Kleine DPS-Kurve (DPS über die Kampfzeit, geglättet): Fläche und Linie in Klassenfarbe, Spitzenwert oben links,
/// beim Überfahren eine senkrechte Linie mit Zeit und DPS.
/// </summary>
public sealed class DpsChart : Grid
{
    private static readonly Brush DimBrush = Frozen(Color.FromRgb(0x9A, 0xA0, 0xAE));
    private readonly Canvas _canvas = new() { ClipToBounds = true, Background = Brushes.Transparent };
    private readonly TextBlock _peak = new() { Foreground = DimBrush, FontSize = 11, Margin = new Thickness(6, 2, 0, 0), IsHitTestVisible = false };
    private readonly TextBlock _hover = new() { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 6, 0), IsHitTestVisible = false };
    private readonly Line _cursor = new() { Stroke = Frozen(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private IReadOnlyList<double> _values = [];
    private Color _color = Color.FromRgb(0x7B, 0x5C, 0xD6);

    /// <summary>Beschriftung eines Punkts (Standard: Kampfzeit m:ss); z. B. Wochentag in der Statistik.</summary>
    public Func<int, string> LabelOf { get; set; } = i => OverlayFormat.Elapsed(TimeSpan.FromSeconds(i));

    /// <summary>Text vor dem Spitzenwert (Standard „Spitze“).</summary>
    public string PeakCaption { get; set; } = "Spitze";

    public DpsChart()
    {
        Height = 120;
        Children.Add(_canvas);
        Children.Add(_peak);
        Children.Add(_hover);
        _canvas.SizeChanged += (_, _) => Draw();
        _canvas.MouseMove += (_, e) => ShowAt(e.GetPosition(_canvas).X);
        _canvas.MouseLeave += (_, _) =>
        {
            _cursor.Visibility = Visibility.Collapsed;
            _hover.Text = string.Empty;
        };
    }

    /// <summary>Neue Werte (DPS je Sekunde, geglättet) und Farbe.</summary>
    public void Update(IReadOnlyList<double> values, Color color)
    {
        _values = values;
        _color = color;
        Draw();
    }

    private void Draw()
    {
        _canvas.Children.Clear();
        var width = _canvas.ActualWidth;
        var height = _canvas.ActualHeight;
        if (_values.Count < 2 || width <= 0 || height <= 0)
        {
            _peak.Text = _values.Count < 2 ? "DPS-Verlauf: noch zu wenig Daten" : string.Empty;
            return;
        }

        var max = Math.Max(1, _values.Max());
        _peak.Text = $"{PeakCaption} {OverlayFormat.PerSecond(max)} · {LabelOf(_values.Count - 1)}";
        var top = 18.0; // Platz für die Beschriftung
        Point At(int i) => new(i * width / (_values.Count - 1), height - (_values[i] / max) * (height - top - 2) - 1);

        // Hilfslinie bei der Hälfte.
        _canvas.Children.Add(new Line { X1 = 0, X2 = width, Y1 = top + (height - top) / 2, Y2 = top + (height - top) / 2, Stroke = Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), StrokeDashArray = [3, 3] });

        var line = new PointCollection(_values.Count);
        for (var i = 0; i < _values.Count; i++) line.Add(At(i));
        var area = new PointCollection(line) { new Point(width, height), new Point(0, height) };
        _canvas.Children.Add(new Polygon
        {
            Points = area,
            Fill = new LinearGradientBrush(Color.FromArgb(0x70, _color.R, _color.G, _color.B), Color.FromArgb(0x05, _color.R, _color.G, _color.B), 90),
        });
        _canvas.Children.Add(new Polyline { Points = line, Stroke = Frozen(_color), StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round });
        _canvas.Children.Add(_cursor);
    }

    private void ShowAt(double x)
    {
        if (_values.Count < 2 || _canvas.ActualWidth <= 0) return;
        var index = (int)Math.Round(Math.Clamp(x / _canvas.ActualWidth, 0, 1) * (_values.Count - 1));
        var cx = index * _canvas.ActualWidth / (_values.Count - 1);
        _cursor.X1 = _cursor.X2 = cx;
        _cursor.Y1 = 0;
        _cursor.Y2 = _canvas.ActualHeight;
        _cursor.Visibility = Visibility.Visible;
        _hover.Text = $"{LabelOf(index)} · {OverlayFormat.PerSecond(_values[index])}";
        _hover.Foreground = Frozen(_color);
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
