using System.Text.Json;

namespace Aion2DpsMeter.Core.Updates;

/// <summary>Ein Eintrag der Patchnotes.</summary>
/// <param name="Version">Version, z. B. „0.0.4“.</param>
/// <param name="Title">Überschrift ohne „## “, z. B. „0.0.4 – 14.10.2026“.</param>
/// <param name="Items">Punkte der Liste (ohne „- “).</param>
public sealed record PatchNote(string Version, string Title, IReadOnlyList<string> Items);

/// <summary>Patchnotes aus CHANGELOG.md („## Version – Datum“, darunter „- Punkt“), neueste zuerst.</summary>
public static class PatchNotes
{
    public static IReadOnlyList<PatchNote> Parse(string markdown)
    {
        var notes = new List<PatchNote>();
        string? version = null, title = null;
        var items = new List<string>();

        void Flush()
        {
            if (version is not null && items.Count > 0) notes.Add(new PatchNote(version, title!, items.ToList()));
            items.Clear();
        }

        foreach (var raw in markdown.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                title = line[3..].Trim();
                var first = title.Split(' ', 2)[0];
                version = UpdateCheck.ParseTag(first) is not null ? first.TrimStart('v', 'V') : null;
            }
            else if (version is not null && line.StartsWith("- ", StringComparison.Ordinal))
            {
                items.Add(line[2..].Trim());
            }
        }

        Flush();
        return notes.OrderByDescending(n => UpdateCheck.ParseTag(n.Version)).ToList();
    }

    /// <summary>
    /// Was soll beim Start gezeigt werden? Alle Einträge neuer als <paramref name="lastSeen"/> bis einschließlich
    /// <paramref name="current"/>; beim allerersten Start (nichts gesehen) nur der aktuelle. Leer, wenn schon gesehen.
    /// </summary>
    public static IReadOnlyList<PatchNote> ToShow(IReadOnlyList<PatchNote> notes, string? lastSeen, string current)
    {
        if (UpdateCheck.ParseTag(current) is not { } cur) return [];
        if (UpdateCheck.ParseTag(lastSeen) is not { } seen) return notes.Where(n => n.Version == current).ToList();
        return notes.Where(n => UpdateCheck.ParseTag(n.Version) is { } v && v.CompareTo(seen) > 0 && v.CompareTo(cur) <= 0).ToList();
    }

    /// <summary>Text für das GitHub-Release (Markdown) eines Eintrags.</summary>
    public static string ToReleaseText(PatchNote note) => string.Join('\n', note.Items.Select(i => "- " + i));
}

/// <summary>
/// Merkt sich in Dokumente\Aion2DpsMeter\version.json, welche Version zuletzt gestartet wurde (für die Patchnotes).
/// Eine fremde Datei mit diesem Namen wird nie überschrieben.
/// </summary>
public sealed class SeenVersionStore(string path)
{
    public const string Marker = "Aion2DpsMeter-Version";

    private sealed record FileContent(string Kind, string LastSeen);

    public string? Load()
    {
        try
        {
            if (!File.Exists(path)) return null;
            var file = JsonSerializer.Deserialize<FileContent>(File.ReadAllText(path));
            return file?.Kind == Marker ? file.LastSeen : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Speichern; false, wenn eine fremde Datei im Weg ist oder das Schreiben scheitert.</summary>
    public bool Save(string version)
    {
        try
        {
            if (File.Exists(path))
            {
                var existing = JsonSerializer.Deserialize<FileContent>(File.ReadAllText(path));
                if (existing?.Kind != Marker) return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new FileContent(Marker, version)));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }
}
