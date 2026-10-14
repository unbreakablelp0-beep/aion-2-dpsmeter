using System.IO;
using System.Text;

namespace Aion2DpsMeter.App;

/// <summary>
/// Schreibt unerwartete Fehler in eine lokale Logdatei, damit Startfehler nicht unsichtbar bleiben.
/// Liegt bewusst in der App (nicht in Core), damit es auch funktioniert, wenn eine andere
/// Bibliothek nicht geladen werden kann. Ist die App-Datei selbst blockiert, läuft auch das nicht.
/// </summary>
public static class StartupErrorLog
{
    /// <summary>Standardordner: %LOCALAPPDATA%\Aion2DpsMeter.</summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aion2DpsMeter");

    public const string FileName = "startfehler.log";

    /// <summary>Hängt einen Eintrag an und gibt den Dateipfad zurück; null, wenn das Schreiben scheitert.</summary>
    public static string? Append(Exception exception, string? directory = null, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        try
        {
            var dir = directory ?? DefaultDirectory;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, FileName);
            File.AppendAllText(path, Format(exception, now ?? DateTimeOffset.Now), Encoding.UTF8);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Text eines Eintrags: Zeit, Fehlertext und alle inneren Fehler.</summary>
    public static string Format(Exception exception, DateTimeOffset time)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"===== {time:yyyy-MM-dd HH:mm:ss zzz} =====");
        sb.AppendLine(exception.ToString());
        sb.AppendLine();
        return sb.ToString();
    }
}
