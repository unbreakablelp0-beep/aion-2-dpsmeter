using Aion2DpsMeter.Core.Capture;

namespace Aion2DpsMeter.Core.Tests.Capture;

public class CaptureStatusTests
{
    [Fact]
    public void NotConfigured_HasExpectedStateAndGermanMessage()
    {
        var status = CaptureStatus.NotConfigured;

        Assert.Equal(CaptureState.NotConfigured, status.State);
        Assert.Equal("Netzwerkaufnahme noch nicht eingerichtet", status.Message);
    }

    [Fact]
    public void Faulted_UsesGermanErrorTextAndKeepsDetail()
    {
        var status = CaptureStatus.Faulted(CaptureErrorKind.AdapterOpenFailed, "pcap: Fehler 31");

        Assert.Equal(CaptureState.Faulted, status.State);
        Assert.Equal(CaptureErrorMessages.Describe(CaptureErrorKind.AdapterOpenFailed), status.Message);
        Assert.Equal("pcap: Fehler 31", status.Detail);
    }

    [Fact]
    public void EveryErrorKind_HasOwnNonEmptyMessage()
    {
        var kinds = Enum.GetValues<CaptureErrorKind>();
        var messages = kinds.Select(CaptureErrorMessages.Describe).ToList();

        Assert.All(messages, m => Assert.False(string.IsNullOrWhiteSpace(m)));
        Assert.Equal(kinds.Length, messages.Distinct().Count());
    }

    [Fact]
    public void CaptureException_WithoutDetail_UsesGermanMessage()
    {
        var ex = new CaptureException(CaptureErrorKind.NpcapNotInstalled);

        Assert.Equal(CaptureErrorKind.NpcapNotInstalled, ex.Kind);
        Assert.Equal(CaptureErrorMessages.Describe(CaptureErrorKind.NpcapNotInstalled), ex.Message);
    }
}
