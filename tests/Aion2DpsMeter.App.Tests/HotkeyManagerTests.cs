using System.Runtime.InteropServices;
using System.Windows.Threading;
using Aion2DpsMeter.App;
using Xunit.Abstractions;

namespace Aion2DpsMeter.App.Tests;

/// <summary>
/// Tastenkürzel: Anmeldung bei Windows (echt, ohne Fokus – nur ein Nachrichtenfenster) und Weiterleitung der
/// Windows-Meldung an die Aktion (Meldung selbst gepostet, kein echter Tastendruck – es werden keine Tasten gesendet).
/// </summary>
public class HotkeyManagerTests(ITestOutputHelper output)
{
    private const int WmHotkey = 0x0312;

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    [Fact]
    public void RegisteredHotkeyMessageRunsAction()
    {
        RunSta(() =>
        {
            using var hotkeys = new HotkeyManager();
            var runs = 0;
            // Ungewöhnliche Kombination, damit sie sicher frei ist: Strg+Alt+Umschalt+F13.
            hotkeys.Register(HotkeyManager.ModControl | HotkeyManager.ModAlt | HotkeyManager.ModShift, 0x7C, "Test", () => runs++);
            Assert.Equal(["Test"], hotkeys.Registered);

            Assert.True(PostMessage(hotkeys.Handle, WmHotkey, new IntPtr(0xB000), IntPtr.Zero));
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            Assert.Equal(1, runs);
        });
    }

    [Fact]
    public void SecondRegistrationOfSameComboIsReportedAsFailed()
    {
        RunSta(() =>
        {
            using var first = new HotkeyManager();
            using var second = new HotkeyManager();
            first.Register(HotkeyManager.ModControl | HotkeyManager.ModAlt | HotkeyManager.ModShift, 0x7D, "A", () => { });
            second.Register(HotkeyManager.ModControl | HotkeyManager.ModAlt | HotkeyManager.ModShift, 0x7D, "B", () => { });
            Assert.Equal(["A"], first.Registered);
            Assert.Equal(["B"], second.Failed);
        });
    }

    /// <summary>Nur Auskunft: sind die Programm-Kürzel auf diesem PC gerade frei? (Belegt, wenn der Meter selbst läuft.)</summary>
    [Fact]
    public void ReportWhetherProgramHotkeysAreFree()
    {
        var registered = new List<string>();
        var failed = new List<string>();
        RunSta(() =>
        {
            using var hotkeys = new HotkeyManager();
            foreach (var (key, name) in new[] { (0x4Bu, "K"), (0x4Fu, "O"), (0x52u, "R"), (0x45u, "E") })
            {
                hotkeys.Register(HotkeyManager.ModControl | HotkeyManager.ModShift, key, $"Strg+Umschalt+{name}", () => { });
            }

            registered.AddRange(hotkeys.Registered);
            failed.AddRange(hotkeys.Failed);
        });

        output.WriteLine($"frei: {string.Join(", ", registered)}");
        output.WriteLine($"belegt: {string.Join(", ", failed)}");
        Assert.Equal(4, registered.Count + failed.Count);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
