using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Aion2DpsMeter.App;

/// <summary>Programm-Symbol (AionMeta.ico, eingebettet) für Fenster, Taskleiste und Tray.</summary>
public static class AppIcon
{
    private static readonly Uri Source = new("pack://application:,,,/AionMeta.ico", UriKind.Absolute);

    /// <summary>Symbol für WPF-Fenster oder null, wenn es nicht geladen werden kann.</summary>
    public static ImageSource? Window { get; } = LoadWindowIcon();

    private static ImageSource? LoadWindowIcon()
    {
        try
        {
            var frame = BitmapFrame.Create(Source);
            frame.Freeze();
            return frame;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or UriFormatException)
        {
            return null;
        }
    }

    /// <summary>Symbol für den Infobereich (Tray) in <paramref name="size"/> Pixeln oder null.</summary>
    public static System.Drawing.Icon? Tray(int size = 32)
    {
        try
        {
            var info = Application.GetResourceStream(Source);
            if (info is null) return null;
            using var stream = info.Stream;
            return new System.Drawing.Icon(stream, size, size);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or UriFormatException)
        {
            return null;
        }
    }
}
