using System.Windows;
using System.Windows.Threading;

namespace Aion2DpsMeter.App;

/// <summary>
/// Einstiegspunkt der Anwendung. Fängt unerwartete Fehler ab, schreibt sie nach
/// %LOCALAPPDATA%\Aion2DpsMeter\startfehler.log und zeigt eine verständliche Meldung.
/// </summary>
public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    /// <summary>
    /// Standard (Endnutzer): nur das Overlay, Aufnahme startet automatisch. Mit dem Argument „--fenster“ öffnet sich
    /// stattdessen das Entwicklerfenster mit Diagnose, .pcap-Aufnahme und Auswertungen.
    /// </summary>
    private void OnStartup(object sender, StartupEventArgs e)
    {
        var main = new MainWindow();
        if (e.Args.Any(a => a.Equals("--fenster", StringComparison.OrdinalIgnoreCase)))
        {
            main.Show();
        }
        else
        {
            main.StartOverlayMode();
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Report(e.Exception);
        e.Handled = true;
        Shutdown(1);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex) Report(ex);
    }

    private static void Report(Exception exception)
    {
        var path = StartupErrorLog.Append(exception);
        var root = exception is System.Windows.Markup.XamlParseException { InnerException: { } inner } ? inner : exception;

        MessageBox.Show(
            "AionMeta wurde wegen eines Fehlers beendet.\n\n" +
            $"{root.Message}\n\n" +
            (path is null ? "Die Logdatei konnte nicht geschrieben werden." : $"Details: {path}"),
            "AionMeta – Fehler",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
