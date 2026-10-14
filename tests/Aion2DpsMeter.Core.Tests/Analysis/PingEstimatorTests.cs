using Aion2DpsMeter.Core.Analysis;

namespace Aion2DpsMeter.Core.Tests.Analysis;

/// <summary>Passiver Ping aus Sequenz/ACK (selbst gebaute Zeitpunkte).</summary>
public class PingEstimatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MeasuresTimeUntilServerAcknowledges()
    {
        var ping = new PingEstimator();
        Assert.Null(ping.Current(T0));
        ping.Sent(1000, 50, T0);                              // Sequenzende 1050
        ping.Acknowledged(1000, T0.AddMilliseconds(5));      // bestätigt noch nicht
        Assert.Null(ping.Current(T0));
        ping.Acknowledged(1050, T0.AddMilliseconds(12));
        Assert.Equal(12, ping.Current(T0.AddSeconds(1))!.Value.TotalMilliseconds);
        Assert.Null(ping.Current(T0.AddSeconds(20)));         // veraltet
    }

    [Fact]
    public void UsesMedianIgnoresRetransmissionsAndWrapsAround()
    {
        var ping = new PingEstimator();
        foreach (var (seq, ms) in new[] { (100u, 10), (200u, 11), (300u, 90), (400u, 12), (500u, 13) })
        {
            var sent = T0.AddSeconds(seq / 100);
            ping.Sent(seq, 10, sent);
            ping.Acknowledged(seq + 10, sent.AddMilliseconds(ms));
        }

        Assert.Equal(12, ping.Current(T0.AddSeconds(6))!.Value.TotalMilliseconds); // Ausreißer 90 ms zählt kaum

        var again = new PingEstimator();
        again.Sent(5000, 10, T0);
        again.Sent(5000, 10, T0.AddMilliseconds(300)); // erneut gesendet → keine Messung
        again.Acknowledged(5010, T0.AddMilliseconds(310));
        Assert.Null(again.Current(T0.AddSeconds(1)));

        var wrap = new PingEstimator();
        wrap.Sent(uint.MaxValue - 4, 10, T0);           // Sequenz läuft über
        wrap.Acknowledged(5, T0.AddMilliseconds(20));
        Assert.Equal(20, wrap.Current(T0.AddSeconds(1))!.Value.TotalMilliseconds);
    }

    [Fact]
    public void ColorLevels()
    {
        Assert.Equal(0, PingEstimator.Level(TimeSpan.FromMilliseconds(10)));
        Assert.Equal(1, PingEstimator.Level(TimeSpan.FromMilliseconds(100)));
        Assert.Equal(2, PingEstimator.Level(TimeSpan.FromMilliseconds(200)));
        Assert.True(new Aion2DpsMeter.Core.Combat.OverlaySettings().Normalized().ShowPing);
    }
}
