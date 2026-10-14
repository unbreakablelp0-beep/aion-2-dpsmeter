using System.Net;
using System.Net.Sockets;
using Aion2DpsMeter.Core.Analysis;
using Aion2DpsMeter.Core.Capture;

namespace Aion2DpsMeter.Capture.Npcap.Tests;

/// <summary>
/// Programmauswahl gegen echtes Npcap und die echte Windows-Verbindungstabelle. Aufgenommen wird nur auf
/// dem Loopback-Adapter (127.0.0.1); den Verkehr erzeugt der Test selbst mit einer eigenen TCP-Verbindung,
/// die erst <b>nach</b> dem Start der Aufnahme geöffnet wird.
/// </summary>
[Trait("Category", "RequiresNpcap")]
public class ProcessSelectionNpcapTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aion2dps-process-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task ConnectionOpenedAfterStart_IsSavedIncludingSyn()
    {
        var backend = new NpcapCaptureBackend();
        if (!backend.GetInfo().IsAvailable) return;
        var loopback = backend.GetAdapters().FirstOrDefault(a => a.IsLoopback);
        if (loopback is null) return;

        var path = Path.Combine(_directory, "programm.pcap");
        var selection = new ProcessSelection(Environment.ProcessId, new WindowsProcessConnectionSource());
        var options = new CaptureOptions("tcp and host 127.0.0.1", path, selection);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)); // Sicherheitsgrenze
        var reported = 0;
        var capture = backend.CaptureAsync(loopback.Id, options, _ => Interlocked.Increment(ref reported), cts.Token);

        await Task.Delay(1000); // Aufnahme läuft, die Verbindung gibt es noch nicht

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var serverPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        int clientPort;
        using (var client = new TcpClient(AddressFamily.InterNetwork))
        {
            await client.ConnectAsync(IPAddress.Loopback, serverPort);
            clientPort = ((IPEndPoint)client.Client.LocalEndPoint!).Port;
            using var accepted = await listener.AcceptTcpClientAsync();
            await client.GetStream().WriteAsync("hallo aufnahme"u8.ToArray());
            var buffer = new byte[64];
            _ = await accepted.GetStream().ReadAsync(buffer);
            await Task.Delay(1500); // Verbindung offen lassen, damit die Tabelle sie sicher sieht
        }

        listener.Stop();
        await Task.Delay(2500); // Zurückhaltezeit abwarten
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture);

        var packets = new List<ParsedPacket>();
        await new PcapFileSource().ReadRawAsync(path, raw =>
        {
            if (PacketHeaderParser.TryParse(raw, out var p) && p is not null) packets.Add(p);
        }, CancellationToken.None);

        // Erlaubt: die Testverbindung und andere Verbindungen dieses Testprozesses (z. B. zum Testrunner).
        var ports = new WindowsProcessConnectionSource().GetConnections()
            .Where(c => c.ProcessId == Environment.ProcessId && c.Protocol == TransportProtocol.Tcp)
            .Select(c => c.LocalPort)
            .Append(serverPort)
            .Append(clientPort)
            .ToHashSet();
        Assert.NotEmpty(packets);
        Assert.All(packets, p => Assert.True(ports.Contains(p.Source.Port) || ports.Contains(p.Destination.Port),
            $"fremdes Paket {p.Source} → {p.Destination}"));
        Assert.Contains(packets, p => p.Source.Port == clientPort && p.Tcp!.Flags == TcpFlags.Syn);
        Assert.Contains(packets, p => p.Payload.Span.SequenceEqual("hallo aufnahme"u8));
        Assert.Equal(packets.Count, reported);
    }
}
