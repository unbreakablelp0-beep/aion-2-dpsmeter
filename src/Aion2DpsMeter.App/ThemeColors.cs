using System.Windows.Media;

namespace Aion2DpsMeter.App;

/// <summary>
/// Farbthema (Akzentfarbe) für Overlay, Einstellungen und Detailfenster: Violett, Türkis, Gold, Schlicht. Klassenfarben
/// der Spielerzeilen bleiben davon unberührt. Fenster lesen die Farbe beim Aufbau, das Overlay beim Umschalten.
/// </summary>
public static class ThemeColors
{
    public static readonly string[] Names = ["Violett", "Türkis", "Gold", "Schlicht"];

    public static Color Accent { get; private set; } = Of("Violett");

    public static Brush AccentBrush => Frozen(Accent);

    /// <summary>Akzent mit Deckkraft (z. B. für Ränder und Tönungen).</summary>
    public static Brush AccentAlpha(byte alpha) => Frozen(Color.FromArgb(alpha, Accent.R, Accent.G, Accent.B));

    public static Color Of(string? theme) => theme switch
    {
        "Türkis" => Color.FromRgb(0x2F, 0xB8, 0xA8),
        "Gold" => Color.FromRgb(0xD8, 0xA8, 0x3C),
        "Schlicht" => Color.FromRgb(0x8A, 0x90, 0x9C),
        _ => Color.FromRgb(0x7B, 0x5C, 0xD6),
    };

    /// <summary>Thema setzen; true, wenn es sich geändert hat.</summary>
    public static bool Set(string? theme)
    {
        var color = Of(theme);
        if (color == Accent) return false;
        Accent = color;
        return true;
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
