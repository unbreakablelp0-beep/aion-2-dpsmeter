using Aion2DpsMeter.Core.Capture;

namespace Aion2DpsMeter.Capture.Npcap.Tests;

/// <summary>
/// Filter und Speichern gegen echtes Npcap. Geöffnet wird nur der Loopback-Adapter mit dem
/// Filter „udp and host 192.0.2.1“ (Dokumentations-IP), der praktisch kein Paket durchlässt – es werden also keine Netzwerkdaten erfasst.
/// </summary>
[Trait("Category", "RequiresNpcap")]
public class CaptureOptionsNpcapTests : IDisposable
{
    // 192.0.2.1 stammt aus einem Dokumentationsbereich und kommt im echten Verkehr nicht vor.
    private const string MatchesNothing = "udp and host 192.0.2.1";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aion2dps-save-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static (NpcapCaptureBackend Backend, NetworkAdapterInfo Loopback)? LoopbackOrNull()
    {
        var backend = new NpcapCaptureBackend();
        if (!backend.GetInfo().IsAvailable) return null;
        var loopback = backend.GetAdapters().FirstOrDefault(a => a.IsLoopback);
        return loopback is null ? null : (backend, loopback);
    }

    [Fact]
    public async Task InvalidFilter_ThrowsInvalidFilter()
    {
        if (LoopbackOrNull() is not (NpcapCaptureBackend backend, NetworkAdapterInfo loopback)) return;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var ex = await Assert.ThrowsAsync<CaptureException>(() =>
            backend.CaptureAsync(loopback.Id, new CaptureOptions("das ist kein filter"), _ => { }, cts.Token));

        Assert.Equal(CaptureErrorKind.InvalidFilter, ex.Kind);
    }

    [Fact]
    public async Task SaveFile_IsCreatedWithPcapHeaderAndCancelEndsCapture()
    {
        if (LoopbackOrNull() is not (NpcapCaptureBackend backend, NetworkAdapterInfo loopback)) return;
        var path = Path.Combine(_directory, "test.pcap");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            backend.CaptureAsync(loopback.Id, new CaptureOptions(MatchesNothing, path), _ => { }, cts.Token));

        var bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal(24, bytes.Length); // nur der pcap-Dateikopf, keine Pakete
        Assert.Equal(0xA1B2C3D4u, BitConverter.ToUInt32(bytes, 0));
    }

    [Fact]
    public async Task SaveFile_ExistingFile_IsNotOverwritten()
    {
        if (LoopbackOrNull() is not (NpcapCaptureBackend backend, NetworkAdapterInfo loopback)) return;
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "vorhanden.pcap");
        await File.WriteAllTextAsync(path, "alt");
        // Zeitlimit, damit ein Fehler den Testlauf nicht endlos blockiert.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var ex = await Assert.ThrowsAsync<CaptureException>(() =>
            backend.CaptureAsync(loopback.Id, new CaptureOptions(MatchesNothing, path), _ => { }, cts.Token));

        Assert.Equal(CaptureErrorKind.SaveFileFailed, ex.Kind);
        Assert.Equal("alt", await File.ReadAllTextAsync(path));
    }
}
