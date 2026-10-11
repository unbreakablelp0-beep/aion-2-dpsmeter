using Aion2DpsMeter.Core.Capture;
using Xunit.Abstractions;

namespace Aion2DpsMeter.Capture.Npcap.Tests;

/// <summary>
/// Integrationstests gegen das echte, installierte Npcap. Sie lesen nur Version und
/// Adapterliste – es wird keine Aufnahme gestartet und nichts gesendet.
/// Das Ergebnis hängt vom PC ab; die Tests prüfen daher nur, dass Fehler sauber
/// eingeordnet werden, und schreiben den Befund in die Testausgabe.
/// </summary>
[Trait("Category", "RequiresNpcap")]
public class NpcapCaptureBackendTests(ITestOutputHelper output)
{
    [Fact]
    public void GetInfo_DoesNotThrowAndReportsReasonWhenUnavailable()
    {
        var info = new NpcapCaptureBackend().GetInfo();

        output.WriteLine($"Verfügbar: {info.IsAvailable}");
        output.WriteLine($"Version:   {info.Version ?? "-"}");
        output.WriteLine($"Fehler:    {info.Error?.ToString() ?? "-"} {info.Detail}");

        if (!info.IsAvailable)
        {
            Assert.NotNull(info.Error);
        }
    }

    [Fact]
    public void GetAdapters_ReturnsAdaptersOrClassifiedError()
    {
        var backend = new NpcapCaptureBackend();
        if (!backend.GetInfo().IsAvailable)
        {
            output.WriteLine("Npcap nicht verfügbar – Adapterliste nicht geprüft.");
            return;
        }

        try
        {
            var adapters = backend.GetAdapters();
            output.WriteLine($"{adapters.Count} Adapter:");
            foreach (var a in adapters)
            {
                output.WriteLine($"- {a.DisplayName} | Loopback={a.IsLoopback} | {string.Join(", ", a.Addresses)}");
                Assert.False(string.IsNullOrWhiteSpace(a.Id));
            }
        }
        catch (CaptureException ex)
        {
            output.WriteLine($"Eingeordneter Fehler: {ex.Kind} – {ex.Message}");
        }
    }

    [Fact]
    public async Task Capture_UnknownAdapter_ThrowsAdapterNotFound()
    {
        var backend = new NpcapCaptureBackend();
        if (!backend.GetInfo().IsAvailable)
        {
            output.WriteLine("Npcap nicht verfügbar – Test übersprungen.");
            return;
        }

        var ex = await Assert.ThrowsAsync<CaptureException>(() =>
            backend.CaptureAsync(@"\Device\NPF_{00000000-GIBT-ES-NICHT}", _ => { }, CancellationToken.None));

        Assert.Equal(CaptureErrorKind.AdapterNotFound, ex.Kind);
    }
}
