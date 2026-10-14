using Aion2DpsMeter.Core.Updates;

namespace Aion2DpsMeter.Core.Tests.Updates;

/// <summary>Patchnotes aus CHANGELOG.md und „Was ist neu“ beim Start.</summary>
public class PatchNotesTests
{
    private const string Sample = """
        # Patchnotes

        ## 0.0.10 – 20.10.2026
        - Zehn

        ## 0.0.9 – 19.10.2026
        - Neun a
        - Neun b

        ## Ohne Version
        - wird ignoriert

        ## 0.0.8 – 18.10.2026
        - Acht
        """;

    [Fact]
    public void ParsesVersionsNewestFirst()
    {
        var notes = PatchNotes.Parse(Sample);
        Assert.Equal(["0.0.10", "0.0.9", "0.0.8"], notes.Select(n => n.Version));
        Assert.Equal(["Neun a", "Neun b"], notes[1].Items);
        Assert.Equal("0.0.9 – 19.10.2026", notes[1].Title);
        Assert.Equal("- Neun a\n- Neun b", PatchNotes.ToReleaseText(notes[1]));
    }

    [Fact]
    public void ShowsWhatIsNewSinceLastStart()
    {
        var notes = PatchNotes.Parse(Sample);
        Assert.Equal(["0.0.10", "0.0.9"], PatchNotes.ToShow(notes, "0.0.8", "0.0.10").Select(n => n.Version)); // Update über zwei Versionen
        Assert.Empty(PatchNotes.ToShow(notes, "0.0.10", "0.0.10"));                                             // schon gesehen
        Assert.Equal(["0.0.9"], PatchNotes.ToShow(notes, null, "0.0.9").Select(n => n.Version));               // erster Start: nur aktuelle
        Assert.Equal(["0.0.9"], PatchNotes.ToShow(notes, "0.0.8", "0.0.9").Select(n => n.Version));            // nichts aus der Zukunft
    }

    [Fact]
    public void ChangelogOfThisRepositoryHasCurrentVersion()
    {
        var dir = new FileInfo(SourceFile()).Directory; // vom Quelltext aus suchen (Build-Ordner kann woanders liegen)
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CHANGELOG.md"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var notes = PatchNotes.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "CHANGELOG.md")));
        Assert.Equal(AppRelease.Current, notes[0].Version); // vor jedem Release Patchnotes eintragen
    }

    private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    [Fact]
    public void SeenVersionStoreNeverOverwritesForeignFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"aion2dps-version-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(directory, "version.json");
            var store = new SeenVersionStore(path);
            Assert.Null(store.Load());
            Assert.True(store.Save("0.0.4"));
            Assert.Equal("0.0.4", store.Load());

            File.WriteAllText(path, "{\"Kind\":\"fremd\"}");
            Assert.False(store.Save("0.0.5"));
            Assert.Equal("{\"Kind\":\"fremd\"}", File.ReadAllText(path));
            Assert.Null(store.Load());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
