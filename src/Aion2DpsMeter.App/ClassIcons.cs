using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Aion2DpsMeter.App;

/// <summary>
/// Einfache, selbst gezeichnete Klassensymbole (Vektor, 24×24) auf einem Kreis in Klassenfarbe. Keine Spiel- oder
/// Fremdgrafiken. Gut lesbar auch klein, weil jedes Symbol nur aus wenigen kräftigen Formen besteht.
/// </summary>
public static class ClassIcons
{
    /// <summary>Gedämpfte Klassenfarben (nur Darstellung).</summary>
    public static readonly IReadOnlyDictionary<string, Color> Colors = new Dictionary<string, Color>
    {
        ["Gladiator"] = Color.FromRgb(0xC0, 0x6A, 0x3A),
        ["Templer"] = Color.FromRgb(0x8A, 0x8F, 0xA6),
        ["Assassine"] = Color.FromRgb(0x9B, 0x5C, 0xC4),
        ["Bogenschütze"] = Color.FromRgb(0x5E, 0xA0, 0x4E),
        ["Magier"] = Color.FromRgb(0x4A, 0x7F, 0xD0),
        ["Beschwörer"] = Color.FromRgb(0x3E, 0xA6, 0xA0),
        ["Kleriker"] = Color.FromRgb(0xD8, 0xB0, 0x40),
        ["Kantor"] = Color.FromRgb(0xC8, 0x5A, 0x7A),
    };

    private static readonly Color Neutral = Color.FromRgb(0x70, 0x74, 0x80);

    // (Pfaddaten, gefüllt?) je Klasse; Koordinaten 0–24.
    private static readonly Dictionary<string, (string Data, bool Fill)[]> Shapes = new()
    {
        // Schwert: Klinge, Parierstange, Griff
        ["Gladiator"] = [("M12,2 L14,5 L14,14 L17.5,14 L17.5,16 L13.5,16 L13.5,20 L14.5,22 L9.5,22 L10.5,20 L10.5,16 L6.5,16 L6.5,14 L10,14 L10,5 Z", true)],
        // Schild
        ["Templer"] = [("M12,2 L20,5 L20,11 C20,16 16.5,19.5 12,22 C7.5,19.5 4,16 4,11 L4,5 Z", true)],
        // Dolch (schräg)
        ["Assassine"] = [("M19.5,3 L20.5,4 L11,14.5 L9.5,13 Z", true), ("M7,12 L12,17 M5.5,18.5 L8.5,15.5", false)],
        // Bogen mit Sehne und Pfeil
        ["Bogenschütze"] = [("M7,2.5 C17,6 17,18 7,21.5 M7,2.5 L7,21.5 M3,12 L20,12 M17,9.5 L20,12 L17,14.5", false)],
        // Stern
        ["Magier"] = [("M12,2 L14.6,8.9 L22,9.3 L16.2,13.9 L18.2,21 L12,17 L5.8,21 L7.8,13.9 L2,9.3 L9.4,8.9 Z", true)],
        // Spirale
        ["Beschwörer"] = [("M12,12 m-1,0 a1,1 0 1,1 2,0 a3,3 0 1,1 -6,0 a5,5 0 1,1 10,0 a7,7 0 1,1 -14,0", false)],
        // Kreuz
        ["Kleriker"] = [("M10,3 H14 V10 H21 V14 H14 V21 H10 V14 H3 V10 H10 Z", true)],
        // Streitkolben
        ["Kantor"] = [("M15.5,8.5 m-4,0 a4,4 0 1,0 8,0 a4,4 0 1,0 -8,0", true), ("M13,11 L5,21", false)],
        // Pfote
        ["Pet"] = [("M12,13 C8.5,13 6.5,16 6.5,18.5 C6.5,20.5 9,21 12,21 C15,21 17.5,20.5 17.5,18.5 C17.5,16 15.5,13 12,13 Z " +
                    "M6,8 m-2,0 a2,2.5 0 1,0 4,0 a2,2.5 0 1,0 -4,0 M18,8 m-2,0 a2,2.5 0 1,0 4,0 a2,2.5 0 1,0 -4,0 " +
                    "M9.5,4.5 m-2,0 a2,2.5 0 1,0 4,0 a2,2.5 0 1,0 -4,0 M14.5,4.5 m-2,0 a2,2.5 0 1,0 4,0 a2,2.5 0 1,0 -4,0", true)],
    };

    /// <summary>Farbe einer Klasse oder neutrales Grau.</summary>
    public static Color ColorOf(string? className) =>
        className is not null && Colors.TryGetValue(className, out var c) ? c : Neutral;

    /// <summary>
    /// Optionaler Ordner für eigene PNG-Symbole (vom Nutzer selbst abgelegt; das Programm liefert keine fremden Grafiken
    /// mit und legt hier nichts an außer – auf Wunsch – den leeren Ordner).
    /// </summary>
    public static string IconDirectory { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Aion2DpsMeter", "Icons");

    /// <summary>Dateinamen (ohne „.png“) je Klasse.</summary>
    public static readonly IReadOnlyDictionary<string, string> FileNames = new Dictionary<string, string>
    {
        ["Kantor"] = "kantor",
        ["Templer"] = "templer",
        ["Assassine"] = "assassine",
        ["Bogenschütze"] = "bogenschuetze",
        ["Magier"] = "magier",
        ["Beschwörer"] = "beschwoerer",
        ["Kleriker"] = "kleriker",
        ["Gladiator"] = "gladiator",
        ["Pet"] = "pet",
        ["Kampfkraft"] = "kampfkraft",
    };

    /// <summary>
    /// Symbol für die Kampfkraft: eigenes PNG (Icon-Ordner, kampfkraft.png), sonst eingebettet (nur Variante
    /// „persoenlich“), sonst ein neutrales Zeichen.
    /// </summary>
    public static FrameworkElement CreateCombatPowerIcon(double size)
    {
        // Eine Bildvorlage nur, wenn sie groß genug ist (sonst wirkt sie matschig) – kleine Ausschnitte ersetzt das Vektorsymbol.
        if ((CustomImage("Kampfkraft") ?? EmbeddedImage("Kampfkraft")) is { } image && SourceWidth("Kampfkraft") >= MinCombatPowerImageWidth)
        {
            var picture = new Image { Source = image, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            return picture;
        }

        return CombatPowerVector(size);
    }

    /// <summary>Selbst gezeichneter Totenkopf (keine Spielgrafik) für die Anzahl der Tode, auf einem 24×24-Raster.</summary>
    public static FrameworkElement SkullVector(double size)
    {
        var bone = Color.FromRgb(0xEC, 0xE6, 0xDA);
        var dark = Color.FromRgb(0x1A, 0x16, 0x1C);
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(new Path
        {
            // Schädel mit Kiefer
            Data = Geometry.Parse("M12,2 C6.5,2 3,5.8 3,10.5 C3,13.6 4.6,15.6 6.5,16.6 L6.5,19.5 C6.5,20.6 7.4,21.5 8.5,21.5 L15.5,21.5 C16.6,21.5 17.5,20.6 17.5,19.5 L17.5,16.6 C19.4,15.6 21,13.6 21,10.5 C21,5.8 17.5,2 12,2 Z"),
            Fill = new LinearGradientBrush(bone, Color.FromRgb(0xB8, 0xB0, 0xA4), 90),
            Stroke = Frozen(Color.FromRgb(0x2A, 0x24, 0x2C)),
            StrokeThickness = 1,
        });
        canvas.Children.Add(new Path { Data = Geometry.Parse("M5.8,11 C5.8,8.9 7.2,8 8.7,8 C10.2,8 11,9.2 10.6,11 C10.3,12.6 9.2,13.4 8.1,13.4 C6.8,13.4 5.8,12.5 5.8,11 Z"), Fill = Frozen(dark) });
        canvas.Children.Add(new Path { Data = Geometry.Parse("M18.2,11 C18.2,8.9 16.8,8 15.3,8 C13.8,8 13,9.2 13.4,11 C13.7,12.6 14.8,13.4 15.9,13.4 C17.2,13.4 18.2,12.5 18.2,11 Z"), Fill = Frozen(dark) });
        canvas.Children.Add(new Path { Data = Geometry.Parse("M12,13.6 L13.3,16 L10.7,16 Z"), Fill = Frozen(dark) });
        canvas.Children.Add(new Path
        {
            // Zähne
            Data = Geometry.Parse("M9.8,18 L9.8,21 M12,18 L12,21 M14.2,18 L14.2,21"),
            Stroke = Frozen(dark),
            StrokeThickness = 1.1,
        });
        return new Viewbox { Width = size, Height = size, Child = canvas, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>Bildvorlagen für die Kampfkraft darunter (in Pixeln) werden nicht verwendet.</summary>
    public const int MinCombatPowerImageWidth = 40;

    /// <summary>
    /// Selbst gezeichnetes Kampfkraft-Symbol (keine Spielgrafik): türkise Raute mit zwei gekreuzten Klingen und einem
    /// kleinen Stein in der Mitte – angelehnt an den Stil im Spiel, gestochen scharf in jeder Größe.
    /// </summary>
    public static FrameworkElement CombatPowerVector(double size)
    {
        // Auf einem festen 24×24-Raster gezeichnet und als Ganzes skaliert (Viewbox), damit nichts verzerrt.
        var teal = Color.FromRgb(0x5F, 0xE0, 0xC8);
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(new Path
        {
            // äußere Raute mit leichtem Schimmer
            Data = Geometry.Parse("M12,1.5 L22.5,12 L12,22.5 L1.5,12 Z"),
            Fill = new LinearGradientBrush(Color.FromArgb(0x70, teal.R, teal.G, teal.B), Color.FromArgb(0x18, teal.R, teal.G, teal.B), 90),
            Stroke = Frozen(teal),
            StrokeThickness = 1.6,
        });
        canvas.Children.Add(new Path
        {
            // vier kurze Strahlen zwischen Rand und Stein
            Data = Geometry.Parse("M12,4.5 L12,7.5 M19.5,12 L16.5,12 M12,19.5 L12,16.5 M4.5,12 L7.5,12"),
            Stroke = Frozen(Color.FromArgb(0xC0, teal.R, teal.G, teal.B)),
            StrokeThickness = 1.4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        });
        canvas.Children.Add(new Path
        {
            // leuchtender Stein in der Mitte
            Data = Geometry.Parse("M12,7.5 L16.5,12 L12,16.5 L7.5,12 Z"),
            Fill = new LinearGradientBrush(Color.FromRgb(0xF2, 0xFF, 0xFC), Color.FromRgb(0x7F, 0xE8, 0xD4), 90),
        });
        return new Viewbox { Width = size, Height = size, Child = canvas, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>Pixelbreite der Bildvorlage (Icon-Ordner oder eingebettet) oder 0.</summary>
    private static int SourceWidth(string key)
    {
        if (!FileNames.TryGetValue(key, out var file)) return 0;
        try
        {
            var path = System.IO.Path.Combine(IconDirectory, file + ".png");
            using var stream = System.IO.File.Exists(path)
                ? System.IO.File.OpenRead(path)
                : typeof(ClassIcons).Assembly.GetManifestResourceStream(EmbeddedPrefix + file + ".png");
            if (stream is null) return 0;
            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(stream, System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation, System.Windows.Media.Imaging.BitmapCacheOption.None);
            return decoder.Frames.Count > 0 ? decoder.Frames[0].PixelWidth : 0;
        }
        catch (Exception ex) when (ex is System.IO.IOException or NotSupportedException or UnauthorizedAccessException or System.IO.FileFormatException)
        {
            return 0;
        }
    }

    private static readonly Dictionary<string, (DateTime Written, System.Windows.Media.Imaging.BitmapSource? Image)> Custom = new();

    /// <summary>Eigenes PNG aus <see cref="IconDirectory"/> oder null (fehlt/ungültig → Vektor-Icon).</summary>
    private static System.Windows.Media.Imaging.BitmapSource? CustomImage(string? key)
    {
        var file = key is not null && FileNames.TryGetValue(key, out var name) ? name : "unbekannt";
        var path = System.IO.Path.Combine(IconDirectory, file + ".png");
        try
        {
            if (!System.IO.File.Exists(path)) return null;
            var written = System.IO.File.GetLastWriteTimeUtc(path);
            if (Custom.TryGetValue(path, out var cached) && cached.Written == written) return cached.Image;

            System.Windows.Media.Imaging.BitmapSource? image = null;
            try
            {
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; // Datei nicht gesperrt halten
                bitmap.UriSource = new Uri(path);
                bitmap.DecodePixelWidth = 64;
                bitmap.EndInit();
                bitmap.Freeze();
                image = bitmap;
            }
            catch (Exception ex) when (ex is NotSupportedException or System.IO.IOException or UriFormatException or InvalidOperationException or System.IO.FileFormatException)
            {
                image = null; // ungültige Datei → Vektor-Icon
            }

            Custom[path] = (written, image);
            return image;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Präfix der Bilder, die nur die Variante „persoenlich“ beim Bauen einbettet (siehe README).</summary>
    public const string EmbeddedPrefix = "PersonalIcons/";

    private static readonly Dictionary<string, System.Windows.Media.Imaging.BitmapSource?> Embedded = new();

    /// <summary>true, wenn diese EXE eingebettete Klassenbilder enthält (nur Variante „persoenlich“).</summary>
    public static bool HasEmbeddedIcons { get; } =
        typeof(ClassIcons).Assembly.GetManifestResourceNames().Any(n => n.StartsWith(EmbeddedPrefix, StringComparison.Ordinal));

    /// <summary>Eingebettetes Bild (Rückfall nach dem Icon-Ordner) oder null – in der Standard-EXE immer null.</summary>
    private static System.Windows.Media.Imaging.BitmapSource? EmbeddedImage(string? key)
    {
        if (!HasEmbeddedIcons || key is null || !FileNames.TryGetValue(key, out var file)) return null;
        if (Embedded.TryGetValue(file, out var cached)) return cached;

        System.Windows.Media.Imaging.BitmapSource? image = null;
        using (var stream = typeof(ClassIcons).Assembly.GetManifestResourceStream(EmbeddedPrefix + file + ".png"))
        {
            if (stream is not null)
            {
                try
                {
                    var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.DecodePixelWidth = 64;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    image = bitmap;
                }
                catch (Exception ex) when (ex is NotSupportedException or System.IO.IOException or InvalidOperationException or System.IO.FileFormatException)
                {
                    image = null;
                }
            }
        }

        Embedded[file] = image;
        return image;
    }

    /// <summary>Symbol in Kreisform. <paramref name="isPet"/> zeigt die Pfote (z. B. „Pet?“-Zeilen).</summary>
    public static FrameworkElement Create(string? className, double size, bool isPet = false)
    {
        var key = isPet ? "Pet" : className;
        if ((CustomImage(key) ?? EmbeddedImage(key)) is { } custom)
        {
            // Dunkle runde Unterlage mit Rand in Klassenfarbe: auch helle Bilder bleiben auf hellem Balken gut erkennbar.
            var holder = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
            var tint = isPet ? Neutral : ColorOf(className);
            holder.Children.Add(new Ellipse
            {
                Fill = Frozen(Color.FromArgb(0xD8, 0x14, 0x12, 0x18)),
                Stroke = Frozen(Color.FromArgb(0xC0, tint.R, tint.G, tint.B)),
                StrokeThickness = Math.Max(1, size / 14),
            });
            holder.Children.Add(new Image { Source = custom, Margin = new Thickness(size * 0.06), Stretch = Stretch.Uniform });
            return holder;
        }

        var grid = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(new Ellipse { Fill = Frozen(isPet ? Neutral : ColorOf(className)) });

        if (key is not null && Shapes.TryGetValue(key, out var shapes))
        {
            var canvas = new Canvas { Width = 24, Height = 24 };
            foreach (var (data, fill) in shapes)
            {
                canvas.Children.Add(new Path
                {
                    Data = Geometry.Parse(data),
                    Fill = fill ? Brushes.White : null,
                    Stroke = fill ? null : Brushes.White,
                    StrokeThickness = fill ? 0 : 2.2,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                });
            }

            grid.Children.Add(new Viewbox { Child = canvas, Margin = new Thickness(size * 0.18) });
        }
        else
        {
            grid.Children.Add(new TextBlock
            {
                Text = "?",
                FontSize = Math.Max(7, size * 0.55),
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        return grid;
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
