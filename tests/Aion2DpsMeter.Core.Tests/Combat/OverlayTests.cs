using Aion2DpsMeter.Core.Combat;

namespace Aion2DpsMeter.Core.Tests.Combat;

/// <summary>Formatierung und Einstellungsdatei des Overlays.</summary>
public class OverlayTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aion2dps-overlay-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Theory]
    [InlineData(0L, "0")]
    [InlineData(950L, "950")]
    [InlineData(999L, "999")]
    [InlineData(1_000L, "1,00K")]
    [InlineData(1_298L, "1,30K")]
    [InlineData(1_341L, "1,34K")]
    [InlineData(9_994L, "9,99K")]
    [InlineData(9_999L, "10,0K")]
    [InlineData(999_949L, "999,9K")]
    [InlineData(999_960L, "1,00M")]
    [InlineData(-1_298L, "-1,30K")]
    [InlineData(12_410L, "12,4K")]
    [InlineData(569_992L, "570,0K")]
    [InlineData(1_880_292L, "1,88M")]
    [InlineData(2_500_000_000L, "2,50G")]
    public void CompactNumbers(long value, string expected)
    {
        Assert.Equal(expected, OverlayFormat.Compact(value));
    }

    [Fact]
    public void BuildShowsTopRowsAndAlwaysOwnCharacter()
    {
        var meter = new LiveCombatMeter();
        meter.ReportPlayer(5, "Ich", isSelf: true);
        for (uint actor = 1; actor <= 5; actor++)
        {
            meter.Add(new CombatHit(T0, actor, 900, 14_000_000 + actor, 1000 * (6 - actor), false, false));
        }

        meter.Add(new CombatHit(T0.AddSeconds(10), 1, 900, 14_000_001, 1, false, false));

        var content = OverlayFormat.Build(meter.Snapshot(T0.AddSeconds(10)), maxRows: 3);

        Assert.Equal(3, content.Rows.Count);
        Assert.Equal(2, content.HiddenRows);
        Assert.Equal("(Name unbekannt) #1", content.Rows[0].Name);
        Assert.Equal("Bogenschütze", content.Rows[0].Class);
        Assert.True(content.Rows[^1].IsOwn); // eigener Charakter ersetzt die letzte Zeile
        Assert.Equal("Ich", content.Rows[^1].Name);
        Assert.StartsWith("Kampf 10 s · 15,0K ·", content.Header);
    }

    [Fact]
    public void OwnOnlyShowsJustOwnRowWithShareOfAll()
    {
        var meter = new LiveCombatMeter();
        meter.ReportPlayer(5, "Ich", isSelf: true);
        meter.Add(new CombatHit(T0, 1, 900, 16_100_004, 3000, false, false));
        meter.Add(new CombatHit(T0, 5, 900, 14_020_340, 1000, false, false));
        meter.Add(new CombatHit(T0.AddSeconds(4), 5, 900, 14_020_340, 1000, false, false));

        var content = OverlayFormat.Build(meter.Snapshot(T0.AddSeconds(4)), maxRows: 8, ownOnly: true);
        var row = Assert.Single(content.Rows);
        Assert.Equal(5u, row.Actor);
        Assert.Equal(2000, row.DamageValue);
        Assert.Equal(500, row.DpsValue);
        Assert.Equal("100,0 %", row.Share); // Anteil an den gezeigten Spielern: nur ich = 100 %
        Assert.Equal(1, content.HiddenRows);
        Assert.True(content.OwnOnly);
        Assert.False(content.OwnUnknown);
        Assert.Equal("500/s", OverlayFormat.PerSecond(row.DpsValue));
        Assert.Equal("28,81K/s", OverlayFormat.PerSecond(28_812));
    }

    [Fact]
    public void OwnOnlyWithoutKnownOwnIsMarkedUnknown()
    {
        var meter = new LiveCombatMeter();
        meter.Add(new CombatHit(T0, 1, 900, 16_100_004, 3000, false, false));

        var content = OverlayFormat.Build(meter.Snapshot(T0), maxRows: 8, ownOnly: true);
        Assert.Empty(content.Rows);
        Assert.True(content.OwnUnknown);
        Assert.Equal(1, content.HiddenRows);
        Assert.Single(OverlayFormat.Build(meter.Snapshot(T0), maxRows: 8).Rows); // „Alle“ zeigt ihn
    }

    [Fact]
    public void TinyRowsAreHiddenButOwnStaysAndPetsAreMarked()
    {
        var meter = new LiveCombatMeter();
        meter.ReportPlayer(9, "Ich", isSelf: true);
        meter.Add(new CombatHit(T0, 1, 900, 16_100_004, 100_000, false, false));
        meter.Add(new CombatHit(T0, 2, 900, 100_044, 500, false, false)); // Pet-typische Fertigkeit, ohne Namen, < 1 %
        meter.Add(new CombatHit(T0, 9, 900, 14_020_340, 300, false, false)); // eigener, < 1 %

        var content = OverlayFormat.Build(meter.Snapshot(T0), maxRows: 8);

        Assert.Equal(2, content.Rows.Count);
        Assert.Equal(1, content.HiddenRows);
        Assert.Equal("Ich", content.Rows[1].Name);

        var pet = meter.Snapshot(T0).Actors.Single(a => a.Actor == 2);
        Assert.True(pet.IsLikelyPet);
        Assert.Equal("Pet? #2", OverlayFormat.DisplayName(pet));
    }

    [Fact]
    public void OwnShownEvenWhenAllRowsAreTiny()
    {
        var meter = new LiveCombatMeter { OwnActor = 9 };
        for (uint a = 1; a <= 200; a++) meter.Add(new CombatHit(T0, a, 900, 14_000_000, 1, false, false));
        meter.Add(new CombatHit(T0, 9, 900, 14_000_000, 0, false, false));

        var content = OverlayFormat.Build(meter.Snapshot(T0), maxRows: 3);

        Assert.Single(content.Rows);
        Assert.True(content.Rows[0].IsOwn);
    }

    [Fact]
    public void TinyRowsCanBeShownAndIdleTimeoutIsAdjustable()
    {
        var meter = new LiveCombatMeter();
        meter.Add(new CombatHit(T0, 1, 900, 14_000_000, 100_000, false, false));
        meter.Add(new CombatHit(T0, 2, 900, 14_000_000, 10, false, false)); // < 1 %

        Assert.Single(OverlayFormat.Build(meter.Snapshot(T0), 8).Rows);
        Assert.Equal(2, OverlayFormat.Build(meter.Snapshot(T0), 8, hideTinyRows: false).Rows.Count);

        meter.IdleTimeout = TimeSpan.FromSeconds(30);
        Assert.True(meter.Snapshot(T0.AddSeconds(25)).Active);
        Assert.Throws<ArgumentOutOfRangeException>(() => meter.IdleTimeout = TimeSpan.Zero);
    }

    [Fact]
    public void NewSettingsRoundTripAndClamp()
    {
        var path = Path.Combine(_directory, "overlay.json");
        var store = new OverlaySettingsStore(path);
        store.Load();
        Assert.True(store.Save(new OverlaySettings(IdleSeconds: 500, HideTinyRows: false)));

        var loaded = new OverlaySettingsStore(path).Load();

        Assert.Equal(120, loaded.IdleSeconds);
        Assert.False(loaded.HideTinyRows);
        Assert.Equal(10, new OverlaySettings(IdleSeconds: 0).Normalized().IdleSeconds);
    }

    [Fact]
    public void BuildWithoutHits()
    {
        var content = OverlayFormat.Build(new LiveCombatMeter().Snapshot(T0), 5);

        Assert.Empty(content.Rows);
        Assert.Contains("noch keine Treffer", content.Header);
    }

    [Fact]
    public void SettingsRoundTripAndNormalize()
    {
        var path = Path.Combine(_directory, "overlay.json");
        var store = new OverlaySettingsStore(path);
        Assert.Equal(new OverlaySettings(), store.Load());
        Assert.Equal(OverlaySettingsFileState.Missing, store.State);

        Assert.True(store.Save(new OverlaySettings(Left: 100, Top: 50, FontSize: 99, BackgroundOpacity: 0, ClickThrough: true)));
        var loaded = new OverlaySettingsStore(path).Load();

        Assert.Equal(100, loaded.Left);
        Assert.Equal(24, loaded.FontSize); // begrenzt
        Assert.Equal(0.1, loaded.BackgroundOpacity);
        Assert.False(loaded.ClickThrough); // wird bewusst nie gespeichert (nach dem Start immer aus)
    }

    [Fact]
    public void ForeignOrBrokenFileIsNeverOverwritten()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "overlay.json");
        File.WriteAllText(path, "{\"Kind\":\"etwas anderes\"}");

        var store = new OverlaySettingsStore(path);
        var settings = store.Load();

        Assert.Equal(OverlaySettingsFileState.Foreign, store.State);
        Assert.Equal(new OverlaySettings(), settings);
        Assert.False(store.Save(settings with { FontSize = 15 }));
        Assert.Equal("{\"Kind\":\"etwas anderes\"}", File.ReadAllText(path));

        File.WriteAllText(path, "kein json");
        var broken = new OverlaySettingsStore(path);
        broken.Load();
        Assert.False(broken.Save(new OverlaySettings()));
        Assert.Equal("kein json", File.ReadAllText(path));
    }

    [Fact]
    public void FileCreatedByOthersAfterLoadIsNotOverwritten()
    {
        var path = Path.Combine(_directory, "overlay.json");
        var store = new OverlaySettingsStore(path);
        store.Load(); // fehlt noch
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "fremd");

        Assert.False(store.Save(new OverlaySettings()));
        Assert.Equal("fremd", File.ReadAllText(path));
    }
}
