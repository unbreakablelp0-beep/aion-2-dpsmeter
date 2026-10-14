using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aion2DpsMeter.Core.Updates;

/// <summary>Version dieses Programms und das Repository, in dem Updates veröffentlicht werden.</summary>
public static class AppRelease
{
    /// <summary>Eigene Version (SemVer, z. B. „0.0.1“) aus Directory.Build.props; der Release-Tag ist „v“ + Version.</summary>
    public static string Current { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var info = typeof(AppRelease).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;
        var version = info?.Split('+')[0];
        return UpdateCheck.ParseTag(version) is not null ? version! : "0.0.1";
    }

    public const string Owner = "unbreakablelp0-beep";
    public const string Repository = "aion-2-dpsmeter";

    /// <summary>GitHub-API: die letzten Releases (daraus wird das höchste gültige SemVer gewählt, nicht blind „latest“).</summary>
    public static readonly Uri ReleasesApi = new($"https://api.github.com/repos/{Owner}/{Repository}/releases?per_page=30");

    /// <summary>Release-Seite im Browser.</summary>
    public static readonly Uri ReleasesPage = new($"https://github.com/{Owner}/{Repository}/releases/latest");
}

/// <summary>Ein verfügbares Update.</summary>
/// <param name="Tag">Versions-Tag, z. B. „v0.0.2“.</param>
/// <param name="Notes">Release-Notizen (Text, gekürzt).</param>
/// <param name="Page">Release-Seite.</param>
/// <param name="AssetName">Dateiname der EXE oder null (keine EXE im Release).</param>
/// <param name="AssetUrl">Download-Adresse der EXE oder null.</param>
/// <param name="AssetSize">Größe laut GitHub in Bytes.</param>
/// <param name="Sha256">Prüfsumme (hex, klein) laut GitHub oder SHA256SUMS, sonst null.</param>
/// <param name="SumsUrl">Adresse einer SHA256SUMS-Datei im Release oder null.</param>
public sealed record UpdateInfo(string Tag, string Notes, Uri Page, string? AssetName, Uri? AssetUrl, long AssetSize, string? Sha256, Uri? SumsUrl)
{
    /// <summary>Kann automatisch installiert werden (EXE von erlaubter Adresse mit bekannter Prüfsumme)?</summary>
    public bool CanInstall => AssetUrl is not null && Sha256 is not null && UpdateCheck.IsAllowedDownload(AssetUrl);
}

/// <summary>
/// Update-Prüfung über die öffentliche GitHub-API (nur lesen; es werden keine Spiel- oder persönlichen Daten gesendet).
/// Hier nur die prüfbare Logik ohne Netz: Antwort lesen, Versionen vergleichen, Adressen und Prüfsummen prüfen.
/// </summary>
public static partial class UpdateCheck
{
    /// <summary>Hosts, von denen eine EXE geladen werden darf (Release-Download und GitHubs Weiterleitungsziele).</summary>
    public static readonly IReadOnlySet<string> AllowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
    };

    /// <summary>
    /// SemVer-Tag „v0.0.1“ oder „0.0.1“ lesen (genau drei Zahlen). Alles andere – alte Tags wie „v43“, Vorabversionen wie
    /// „0.0.2-beta“ – ergibt null und wird ignoriert.
    /// </summary>
    public static (int Major, int Minor, int Patch)? ParseTag(string? tag)
    {
        if (tag is null) return null;
        var m = TagPattern().Match(tag.Trim());
        if (!m.Success) return null;
        static int N(Group g) => int.Parse(g.Value, System.Globalization.CultureInfo.InvariantCulture);
        return (N(m.Groups[1]), N(m.Groups[2]), N(m.Groups[3]));
    }

    /// <summary>Ist <paramref name="tag"/> neuer als <paramref name="current"/>? Unlesbare Tags gelten nie als neuer.</summary>
    public static bool IsNewer(string? tag, string current) =>
        ParseTag(tag) is { } t && ParseTag(current) is { } c && t.CompareTo(c) > 0;

    /// <summary>
    /// Antwort von „releases“ (Liste) lesen: das Release mit dem höchsten gültigen SemVer-Tag (ohne Entwürfe und
    /// Vorabversionen) oder null.
    /// </summary>
    public static UpdateInfo? ParseReleases(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            UpdateInfo? best = null;
            foreach (var release in doc.RootElement.EnumerateArray())
            {
                if (ParseRelease(release) is not { } info) continue;
                if (best is null || ParseTag(info.Tag)!.Value.CompareTo(ParseTag(best.Tag)!.Value) > 0) best = info;
            }

            return best;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Nur HTTPS und nur die erlaubten Hosts; Release-Downloads auf github.com nur aus diesem Repository.</summary>
    public static bool IsAllowedDownload(Uri url)
    {
        if (url.Scheme != Uri.UriSchemeHttps || !AllowedHosts.Contains(url.Host)) return false;
        return !url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
               url.AbsolutePath.StartsWith($"/{AppRelease.Owner}/{AppRelease.Repository}/releases/download/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Ein einzelnes Release lesen. Null, wenn es nicht passt (kein SemVer-Tag, Entwurf, Vorabversion).</summary>
    public static UpdateInfo? ParseLatest(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return ParseRelease(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static UpdateInfo? ParseRelease(JsonElement root)
    {
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("tag_name", out var tagElement)) return null;
            if (Bool(root, "draft") || Bool(root, "prerelease")) return null;
            var tag = tagElement.GetString();
            if (ParseTag(tag) is null) return null;
            var page = Str(root, "html_url") is { } html && Uri.TryCreate(html, UriKind.Absolute, out var p) && p.Scheme == Uri.UriSchemeHttps ? p : AppRelease.ReleasesPage;
            var notes = Str(root, "body") ?? string.Empty;
            if (notes.Length > 2_000) notes = notes[..2_000] + " …";

            string? assetName = null, sha = null;
            Uri? assetUrl = null, sumsUrl = null;
            long size = 0;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = Str(asset, "name");
                    var url = Str(asset, "browser_download_url");
                    if (name is null || url is null || !Uri.TryCreate(url, UriKind.Absolute, out var u)) continue;
                    if (name.Equals("SHA256SUMS", StringComparison.OrdinalIgnoreCase) || name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        sumsUrl = u;
                        continue;
                    }

                    if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || assetName is not null) continue;
                    assetName = name;
                    assetUrl = u;
                    size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var sv) ? sv : 0;
                    // Neuere GitHub-Antworten enthalten die Prüfsumme direkt („digest“: „sha256:…“).
                    if (Str(asset, "digest") is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) sha = NormalizeSha(digest[7..]);
                }
            }

            return new UpdateInfo(tag!, notes, page, assetName, assetUrl, size, sha, sumsUrl);
        }
    }

    /// <summary>Prüfsumme für <paramref name="fileName"/> aus einer SHA256SUMS-Datei („hex  name“ je Zeile) oder null.</summary>
    public static string? FindSha256(string sums, string fileName)
    {
        foreach (var line in sums.Split('\n'))
        {
            var parts = line.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[1].TrimStart('*').Trim().Equals(fileName, StringComparison.OrdinalIgnoreCase)) return NormalizeSha(parts[0]);
        }

        return null;
    }

    /// <summary>Stimmt die SHA-256 von <paramref name="data"/> mit <paramref name="expected"/> überein?</summary>
    public static bool MatchesSha256(ReadOnlySpan<byte> data, string expected) =>
        NormalizeSha(expected) is { } want && Convert.ToHexStringLower(SHA256.HashData(data)) == want;

    /// <summary>Sieht <paramref name="data"/> wie ein Windows-Programm aus (MZ-Kopf)?</summary>
    public static bool LooksLikeExe(ReadOnlySpan<byte> data) => data.Length > 1024 && data[0] == (byte)'M' && data[1] == (byte)'Z';

    /// <summary>Die ersten Zeilen der Release-Notizen für den Hinweis (ohne Markdown-Überschriftenzeichen).</summary>
    public static string ShortNotes(string notes, int maxLines = 8) =>
        string.Join('\n', notes.Replace("\r", "").Split('\n').Select(l => l.Trim().TrimStart('#').Trim()).Where(l => l.Length > 0).Take(maxLines));

    private static string? NormalizeSha(string hex)
    {
        var h = hex.Trim().ToLowerInvariant();
        return h.Length == 64 && h.All(Uri.IsHexDigit) ? h : null;
    }

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    [GeneratedRegex(@"^[vV]?(\d{1,6})\.(\d{1,6})\.(\d{1,6})$")]
    private static partial Regex TagPattern();
}

/// <summary>
/// Tauscht die laufende EXE aus: neue Datei neben die alte legen, alte in „.old“ umbenennen (Windows erlaubt das
/// Umbenennen einer laufenden EXE), neue an ihren Platz. Die „.old“ räumt der nächste Start weg.
/// </summary>
public static class UpdateInstaller
{
    public static string NewPath(string exePath) => exePath + ".new";

    public static string OldPath(string exePath) => exePath + ".old";

    /// <summary>Neue EXE ablegen und austauschen. Bei einem Fehler bleibt die alte EXE an ihrem Platz.</summary>
    public static void Install(string exePath, byte[] data)
    {
        var next = NewPath(exePath);
        var old = OldPath(exePath);
        File.WriteAllBytes(next, data);
        if (File.Exists(old)) File.Delete(old); // Rest eines früheren Updates (eigene Datei)
        File.Move(exePath, old);
        try
        {
            File.Move(next, exePath);
        }
        catch
        {
            File.Move(old, exePath); // zurück: alte EXE wieder an ihren Platz
            throw;
        }
    }

    /// <summary>Beim Start: „.old“ und liegengebliebene „.new“ eines früheren Updates entfernen (Fehler still ignorieren).</summary>
    public static void CleanUp(string exePath)
    {
        foreach (var path in new[] { OldPath(exePath), NewPath(exePath) })
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // läuft evtl. noch kurz – beim nächsten Start erneut
            }
        }
    }
}
