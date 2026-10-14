namespace Aion2DpsMeter.Core.Analysis;

/// <summary>
/// Ping (Round-Trip-Time) zum Spielserver, rein passiv aus den mitgelesenen TCP-Kopfzeilen: Zeit vom eigenen Paket mit
/// Nutzdaten (Sequenzende) bis zur Bestätigung (ACK) des Servers. Es wird nichts gesendet. Wiederholte Pakete zählen nicht
/// (Karn), angezeigt wird der Median der letzten Messungen.
/// </summary>
public sealed class PingEstimator
{
    private const int MaxPending = 128;
    private const int Window = 25;

    private readonly object _lock = new();
    private readonly List<(uint SeqEnd, DateTimeOffset Sent, bool Valid)> _pending = [];
    private readonly Queue<(DateTimeOffset At, double Ms)> _samples = new();

    /// <summary>Eigenes Paket an den Server (nur mit Nutzdaten).</summary>
    public void Sent(uint sequence, int payloadLength, DateTimeOffset time)
    {
        if (payloadLength <= 0) return;
        var end = unchecked(sequence + (uint)payloadLength);
        lock (_lock)
        {
            var i = _pending.FindIndex(p => p.SeqEnd == end);
            if (i >= 0)
            {
                _pending[i] = _pending[i] with { Valid = false }; // erneut gesendet → keine Messung
                return;
            }

            _pending.Add((end, time, true));
            if (_pending.Count > MaxPending) _pending.RemoveAt(0);
        }
    }

    /// <summary>Bestätigung vom Server: alle bis <paramref name="ack"/> bestätigten Pakete auswerten.</summary>
    public void Acknowledged(uint ack, DateTimeOffset time)
    {
        lock (_lock)
        {
            DateTimeOffset? newest = null;
            var allValid = true;
            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                var p = _pending[i];
                if (unchecked((int)(ack - p.SeqEnd)) < 0) continue; // noch nicht bestätigt
                if (newest is null || p.Sent > newest)
                {
                    newest = p.Sent;
                    allValid = p.Valid;
                }

                _pending.RemoveAt(i);
            }

            if (newest is not { } sent || !allValid) return;
            var ms = (time - sent).TotalMilliseconds;
            if (ms is < 0 or > 5_000) return;
            _samples.Enqueue((time, ms));
            while (_samples.Count > Window) _samples.Dequeue();
        }
    }

    /// <summary>Aktueller Ping (Median der letzten Messungen) oder null, wenn seit 15 s nichts gemessen wurde.</summary>
    public TimeSpan? Current(DateTimeOffset now)
    {
        lock (_lock)
        {
            if (_samples.Count == 0 || now - _samples.Last().At > TimeSpan.FromSeconds(15)) return null;
            var sorted = _samples.Select(s => s.Ms).OrderBy(m => m).ToList();
            return TimeSpan.FromMilliseconds(sorted[sorted.Count / 2]);
        }
    }

    /// <summary>Farbstufe: 0 = gut (unter 80 ms), 1 = mittel (unter 150 ms), 2 = schlecht.</summary>
    public static int Level(TimeSpan ping) => ping.TotalMilliseconds < 80 ? 0 : ping.TotalMilliseconds < 150 ? 1 : 2;
}
