using System.Net.Http;
using System.Text;
using Aion2DpsMeter.Core.Combat;

namespace Aion2DpsMeter.App;

/// <summary>
/// Sendet eine Kampf-Zusammenfassung an die Bestenliste – nur wenn der Spieler zugestimmt hat und ein Schlüssel
/// eingetragen ist. Fehler (offline, Server) werden still ignoriert; es wird nichts wiederholt oder zwischengespeichert.
/// </summary>
public sealed class StatsShareService : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public async Task ShareAsync(SharedFight fight)
    {
        if (!StatsShareConfig.Configured) return;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, StatsShareConfig.InsertEndpoint)
            {
                Content = new StringContent(fight.ToJson(), Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("apikey", StatsShareConfig.AnonKey);
            request.Headers.Add("Authorization", $"Bearer {StatsShareConfig.AnonKey}");
            request.Headers.Add("Prefer", "return=minimal");
            using var _ = await _http.SendAsync(request).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            // still: Teilen ist ein Zusatz, der Meter läuft ohne weiter
        }
    }

    public void Dispose() => _http.Dispose();
}
