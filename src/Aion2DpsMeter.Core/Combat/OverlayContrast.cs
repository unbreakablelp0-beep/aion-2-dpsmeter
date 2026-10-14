namespace Aion2DpsMeter.Core.Combat;

/// <summary>Farben und Mindestwerte einer Kontraststufe des Overlays.</summary>
/// <param name="Secondary">Farbe der Nebentexte (Schaden, Anteil, Hinweise) als RGB.</param>
/// <param name="Name">Farbe der Spielernamen als RGB.</param>
/// <param name="MinOpacity">Mindest-Deckkraft des Hintergrunds (der Regler kann nicht darunter).</param>
/// <param name="Shadow">Dunkler Schatten um die Schrift.</param>
public sealed record ContrastStyle(int Secondary, int Name, double MinOpacity, bool Shadow);

/// <summary>Kontraststufen und Kontrastverhältnis nach WCAG 2 (relative Leuchtdichte).</summary>
public static class OverlayContrast
{
    public const string Normal = "Normal";
    public const string High = "Hoch";
    public const string VeryHigh = "Sehr hoch";

    /// <summary>Wählbare Stufen.</summary>
    public static readonly string[] Levels = [Normal, High, VeryHigh];

    /// <summary>Hintergrundfarbe des Overlays (RGB).</summary>
    public const int Panel = 0x12141A;

    public static ContrastStyle For(string? level) => level switch
    {
        Normal => new ContrastStyle(0x9AA0AE, 0xD2D6DE, 0.1, false),
        VeryHigh => new ContrastStyle(0xECEEF2, 0xFFFFFF, 0.9, true),
        _ => new ContrastStyle(0xC8CDD6, 0xF0F2F6, 0.75, true),
    };

    /// <summary>Wirksame Deckkraft: Reglerwert, aber nicht unter der Mindest-Deckkraft der Stufe.</summary>
    public static double EffectiveOpacity(double setting, string? level) => Math.Max(setting, For(level).MinOpacity);

    /// <summary>Farbe des Overlay-Hintergrunds mit Deckkraft <paramref name="opacity"/> über <paramref name="behind"/>.</summary>
    public static int Composite(double opacity, int behind)
    {
        int Mix(int shift) => (int)Math.Round(opacity * ((Panel >> shift) & 0xFF) + (1 - opacity) * ((behind >> shift) & 0xFF));
        return Mix(16) << 16 | Mix(8) << 8 | Mix(0);
    }

    /// <summary>Kontrastverhältnis zweier RGB-Farben (1–21).</summary>
    public static double Ratio(int a, int b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(int rgb)
    {
        static double Channel(int c)
        {
            var s = c / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel((rgb >> 16) & 0xFF) + 0.7152 * Channel((rgb >> 8) & 0xFF) + 0.0722 * Channel(rgb & 0xFF);
    }
}
