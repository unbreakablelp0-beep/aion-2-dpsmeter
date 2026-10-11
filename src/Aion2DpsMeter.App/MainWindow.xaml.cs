using System.Windows;
using Aion2DpsMeter.Core.Capture;

namespace Aion2DpsMeter.App;

/// <summary>
/// Hauptfenster. Zeigt vorerst nur den Status der Netzwerkaufnahme.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        StatusText.Text = CaptureStatus.NotConfigured.Message;
    }
}
