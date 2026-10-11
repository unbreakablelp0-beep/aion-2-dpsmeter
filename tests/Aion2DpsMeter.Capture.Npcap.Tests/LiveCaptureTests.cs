using Aion2DpsMeter.Core.Capture;
using Xunit.Abstractions;

namespace Aion2DpsMeter.Capture.Npcap.Tests;

/// <summary>
/// Echte, rein passive Kurzaufnahme. Läuft nur, wenn sie ausdrücklich eingeschaltet wird:
/// Umgebungsvariable AION2DPS_LIVE_CAPTURE_ADAPTER = Teil der Adapterbeschreibung (z. B. "I225-V").
/// Es wird nur gezählt (Pakete, Bytes, Zeitspanne); kein Paketinhalt wird gespeichert.
/// </summary>
[Trait("Category", "LiveCapture")]
[Trait("Category", "RequiresNpcap")]
public class LiveCaptureTests(ITestOutputHelper output)
{
    private const string AdapterVariable = "AION2DPS_LIVE_CAPTURE_ADAPTER";
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task PassiveCapture_TenSeconds_CountsPackets()
    {
        var adapterFilter = Environment.GetEnvironmentVariable(AdapterVariable);
        if (string.IsNullOrWhiteSpace(adapterFilter))
        {
            output.WriteLine($"Nicht ausgeführt: {AdapterVariable} ist nicht gesetzt.");
            return;
        }

        var diagnostics = new NetworkDiagnostics(new NpcapCaptureBackend());
        var states = new List<string>();
        diagnostics.StatusChanged += (_, s) => { lock (states) states.Add($"{s.State}: {s.Message}"); };

        var init = diagnostics.Initialize();
        Assert.Equal(CaptureState.Ready, init.State);

        var adapter = Assert.Single(diagnostics.Adapters,
            a => a.Description.Contains(adapterFilter, StringComparison.OrdinalIgnoreCase));

        diagnostics.Start(adapter.Id);
        await Task.Delay(Duration);
        await diagnostics.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));

        var stats = diagnostics.Statistics;
        output.WriteLine($"Adapter:   {adapter.DisplayName}");
        output.WriteLine($"Dauer:     {Duration.TotalSeconds} s");
        output.WriteLine($"Pakete:    {stats.PacketCount}");
        output.WriteLine($"Bytes:     {stats.ByteCount}");
        output.WriteLine($"Erstes:    {stats.FirstPacketAt:O}");
        output.WriteLine($"Letztes:   {stats.LastPacketAt:O}");
        output.WriteLine("Status-Verlauf:");
        foreach (var s in states) output.WriteLine($"  {s}");

        Assert.Equal(CaptureState.Stopped, diagnostics.Status.State);
    }
}
