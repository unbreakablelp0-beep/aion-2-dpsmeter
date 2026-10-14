using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Aion2DpsMeter.Core.Updates;

namespace Aion2DpsMeter.App;

/// <summary>
/// Fragt die öffentliche GitHub-API nach dem neuesten Release (nur lesen, keine Spiel- oder persönlichen Daten) und lädt
/// auf Klick die EXE von einer erlaubten Adresse, prüft Größe, Programmkopf und SHA-256 und tauscht die EXE aus.
/// Fehler (offline, Rate-Limit) werden still ignoriert.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private readonly HttpClient _http;

    public UpdateService()
    {
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 }) { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"Aion2DpsMeter/{AppRelease.Current}");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    /// <summary>Höchstes gültiges Release, wenn es neuer ist; sonst null (keins, gleich alt, Fehler).</summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(AppRelease.ReleasesApi, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var info = UpdateCheck.ParseReleases(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            if (info is null || !UpdateCheck.IsNewer(info.Tag, AppRelease.Current)) return null;

            // Keine Prüfsumme direkt von GitHub: SHA256SUMS aus dem Release lesen, falls vorhanden.
            if (info is { Sha256: null, SumsUrl: { } sums, AssetName: { } name } && UpdateCheck.IsAllowedDownload(sums))
            {
                var text = await _http.GetStringAsync(sums, cancellationToken).ConfigureAwait(false);
                info = info with { Sha256 = UpdateCheck.FindSha256(text, name) };
            }

            return info;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// EXE laden (mit Fortschritt 0–1), prüfen und installieren. Wirft bei jedem Problem eine Ausnahme mit verständlicher
    /// Meldung; die laufende EXE bleibt dann unverändert.
    /// </summary>
    public async Task InstallAsync(UpdateInfo info, string exePath, IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (!info.CanInstall || info.AssetUrl is not { } url || info.Sha256 is not { } sha)
        {
            throw new InvalidOperationException("Für dieses Release fehlt die EXE oder ihre Prüfsumme.");
        }

        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is { } final && !UpdateCheck.IsAllowedDownload(final))
        {
            throw new InvalidOperationException($"Download wurde an eine nicht erlaubte Adresse umgeleitet ({final.Host}).");
        }

        var total = response.Content.Headers.ContentLength ?? info.AssetSize;
        if (total is <= 0 or > 500_000_000) total = info.AssetSize;
        using var buffer = new MemoryStream();
        await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        {
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > 500_000_000) throw new InvalidOperationException("Download ist unerwartet groß.");
                if (total > 0) progress.Report(Math.Min(1, (double)buffer.Length / total));
            }
        }

        var data = buffer.ToArray();
        if (info.AssetSize > 0 && data.Length != info.AssetSize) throw new InvalidOperationException("Download unvollständig (Größe passt nicht).");
        if (!UpdateCheck.LooksLikeExe(data)) throw new InvalidOperationException("Download ist kein Windows-Programm.");
        if (!UpdateCheck.MatchesSha256(data, sha)) throw new InvalidOperationException("Prüfsumme stimmt nicht – Update verworfen.");
        UpdateInstaller.Install(exePath, data);
    }

    /// <summary>Pfad der laufenden EXE, wenn ein Austausch möglich ist (eigenständige EXE), sonst null.</summary>
    public static string? InstallableExePath()
    {
        var path = Environment.ProcessPath;
        if (path is null || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        // Beim Entwickeln (dotnet run, Debug-Ordner mit DLLs daneben) nicht austauschen.
        return File.Exists(Path.ChangeExtension(path, ".dll")) ? null : path;
    }

    /// <summary>Release-Seite im Standardbrowser öffnen.</summary>
    public static void OpenReleasePage(Uri page)
    {
        var target = page.Scheme == Uri.UriSchemeHttps && page.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ? page : AppRelease.ReleasesPage;
        Process.Start(new ProcessStartInfo(target.AbsoluteUri) { UseShellExecute = true });
    }

    public void Dispose() => _http.Dispose();
}
